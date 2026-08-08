// Textured and tinted, unlit. Text, item sprites, maps, paintings, the loading screen.
// Uniforms are the same block gbuffers_basic.wgsl declares — see there for what params holds.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
    tint: vec4<f32>,
    params: vec4<f32>,
}

@group(0) @binding(0) var<uniform> u: Uniforms;

@group(1) @binding(0) var t: texture_2d<f32>;
@group(1) @binding(1) var s: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(2) texcoord: vec2<f32>,
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texcoord: vec2<f32>,
}

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.position = u.projectionMatrix * u.modelViewMatrix * vec4(in.position, 1.0);
    // One or the other — see gbuffers_basic.wgsl.
    out.color = select(u.tint, in.color, u.params.x > 0.5);
    out.texcoord = in.texcoord;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let sampled = in.color * textureSample(t, s, in.texcoord);

    if (sampled.a < u.params.y) {
        discard;
    }

    return sampled;
}
