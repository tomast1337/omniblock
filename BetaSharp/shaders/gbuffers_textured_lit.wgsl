// Textured, tinted geometry — the WGSL equivalent of gbuffers_textured.{vert,frag}.
// Identical to gbuffers_textured_lit.wgsl and deliberately a separate file: a pack overrides one
// without the other, so they cannot be an include. What tells them apart is which draws carry which
// name, not what the default program does.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
    textureMatrix: mat4x4<f32>,
    normalMatrix: mat3x3<f32>,
    ambientDarkness: f32,
    luminanceOffset: f32,
    lightingEnabled: u32,
    alphaThreshold: f32,
    shadeModel: u32,
    fogEnabled: u32,
    fogMode: u32,
    fogStart: f32,
    fogEnd: f32,
    fogDensity: f32,
    fogColor: vec4<f32>,
    light0Direction: vec4<f32>,
    light0Diffuse: vec4<f32>,
    light1Direction: vec4<f32>,
    light1Diffuse: vec4<f32>,
    ambientLight: vec4<f32>,
};

@group(0) @binding(0) var<uniform> u: Uniforms;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texcoord: vec2<f32>,
    @location(3) normal: vec3<f32>,
    @location(4) @interpolate(flat) arrayLayer: i32,
    @location(5) @interpolate(flat) light: vec2<u32>,
};

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) colorFlat: vec4<f32>,
    @location(1) colorSmooth: vec4<f32>,
    @location(2) texcoord: vec2<f32>,
    @location(3) fogDistance: f32,
    @location(4) @interpolate(flat) arrayLayer: i32,
};

fn rampLuminance(level: f32) -> f32 {
    let factor = 1.0 - level / 15.0;
    return (1.0 - factor) / (factor * 3.0 + 1.0) * (1.0 - u.luminanceOffset) + u.luminanceOffset;
}

fn worldBrightness(packedLight: vec2<u32>) -> f32 {
    let sky = f32(packedLight.x) * 0.25 - u.ambientDarkness;
    let block = f32(packedLight.y) * 0.25;
    return rampLuminance(clamp(max(sky, block), 0.0, 15.0));
}

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var viewPos = u.modelViewMatrix * vec4<f32>(in.position, 1.0);
    var out: VertexOutput;
    out.position = u.projectionMatrix * viewPos;
    out.arrayLayer = in.arrayLayer;
    out.fogDistance = length(viewPos.xyz);
    out.texcoord = (u.textureMatrix * vec4<f32>(in.texcoord, 0.0, 1.0)).xy;

    var tint = vec4<f32>(in.color.rgb * worldBrightness(in.light), in.color.a);
    if (u.lightingEnabled != 0u) {
        let n = normalize(u.normalMatrix * in.normal);
        let diffuse0 = max(dot(n, u.light0Direction.xyz), 0.0);
        let diffuse1 = max(dot(n, u.light1Direction.xyz), 0.0);
        let lit = u.ambientLight.xyz + diffuse0 * u.light0Diffuse.xyz + diffuse1 * u.light1Diffuse.xyz;
        tint = vec4<f32>(clamp(in.color.rgb * lit, vec3(0.0), vec3(1.0)), in.color.a);
    }

    out.colorFlat = tint;
    out.colorSmooth = tint;
    return out;
}

@fragment
fn fs_main(
    in: VertexOutput,
    @location(0) @interpolate(flat) colorFlatIn: vec4<f32>,
) -> @location(0) vec4<f32> {
    var tint = u.shadeModel == 0u ? colorFlatIn : in.colorSmooth;
    var color = tint;

    // Texture sampling deferred until the array and 2D samplers are bound (Phase 4).
    // For now, the tint alone is the output.

    if (color.a < u.alphaThreshold) {
        discard;
    }

    if (u.fogEnabled != 0u) {
        let fogFactor = u.fogMode == 0u
            ? clamp((u.fogEnd - in.fogDistance) / (u.fogEnd - u.fogStart), 0.0, 1.0)
            : clamp(exp(-u.fogDensity * in.fogDistance), 0.0, 1.0);
        color = mix(u.fogColor, color, vec4(fogFactor));
    }

    return color;
}
