// The debug overlay, drawn from ImDrawData: a textured, vertex-coloured triangle list in screen
// space. Hexa.NET.ImGui ships an OpenGL3 backend and no WebGPU one, so this is the whole of what
// that backend's GLSL did.

struct Uniforms {
    // Screen space to clip space. Built on the CPU because ImGui's framebuffer size is only known
    // per frame, and because the Z range differs from the GL formulation (see ImGuiWgpuBackend).
    projection: mat4x4<f32>,
};

@group(0) @binding(0) var<uniform> uniforms: Uniforms;

@group(1) @binding(0) var atlas: texture_2d<f32>;
@group(1) @binding(1) var atlasSampler: sampler;

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
};

@vertex
fn vs_main(
    @location(0) position: vec2<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) color: vec4<f32>,
) -> VertexOutput {
    var out: VertexOutput;
    out.position = uniforms.projection * vec4<f32>(position, 0.0, 1.0);
    out.uv = uv;
    out.color = color;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    return in.color * textureSample(atlas, atlasSampler, in.uv);
}
