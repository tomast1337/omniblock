// Sky dome, sunrise/sunset fan, sun, moon, stars.
// One shader for all four — the draw picks the mode with uniforms rather than switching programs.
//
// Geometry from the Tessellator seam, same vertex layout as gbuffers_textured:
// location 0 = position (float32x3), 1 = colour (unorm8x4), 2 = texcoord (float32x2).
// A draw carrying no per-vertex colour or texcoords still has those bytes in the buffer; the
// useVertexColor uniform says which to read.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
    tint: vec4<f32>,
    skyColor: vec3<f32>,
    // 4 bytes padding — vec3 takes 16 bytes in a uniform block
    groundColor: vec3<f32>,
    // 4 bytes padding
    fogStart: f32,
    fogEnd: f32,
    gradientMode: u32,
    useTexture: u32,
    useVertexColor: u32,
}

@group(0) @binding(0) var<uniform> u: Uniforms;

@group(1) @binding(0) var t_sky: texture_2d<f32>;
@group(1) @binding(1) var s_sky: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texcoord: vec2<f32>,
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texcoord: vec2<f32>,
    @location(2) localPos: vec3<f32>,
    @location(3) fogDist: f32,
}

const PIH2: f32 = 0.7853981634; // PI / 4

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.position = u.projectionMatrix * u.modelViewMatrix * vec4(in.position, 1.0);
    out.color = select(u.tint, in.color, u.useVertexColor > 0u);
    out.texcoord = in.texcoord;
    out.localPos = in.position;
    out.fogDist = length(in.position.xz);
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let fogRange = max(u.fogEnd - u.fogStart, 0.001);
    let fogFactor = clamp((u.fogEnd - in.fogDist) / fogRange, 0.0, 1.0);

    var color: vec4<f32>;

    if (u.useTexture != 0u) {
        // Sun and moon: textured quad, tinted by vertex colour or the uniform.
        color = textureSample(t_sky, s_sky, in.texcoord) * in.color;
    } else if (u.gradientMode != 0u) {
        // Sky dome: elevation-based gradient between ground and sky.
        // elevation: PI/2 = zenith, 0 = horizon, -PI/2 = nadir
        let horizDist = length(in.localPos.xz);
        let elevation = atan2(in.localPos.y, max(horizDist, 0.001));
        let t = clamp(elevation / PIH2 + 0.5, 0.0, 1.0);
        let gradColor = mix(u.groundColor, u.skyColor, t);
        color = vec4(gradColor, fogFactor);
    } else {
        // Sunrise fan and stars: vertex colour with fog on alpha.
        color = in.color;
        color.a *= fogFactor;
    }

    if (color.a < 0.001) {
        discard;
    }

    return color;
}
