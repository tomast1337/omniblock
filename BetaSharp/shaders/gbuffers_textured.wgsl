struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
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
    out.color = in.color;
    out.texcoord = in.texcoord;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    return in.color * textureSample(t, s, in.texcoord);
}
