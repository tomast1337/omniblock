// The root of the fallback chain: untextured, unlit geometry, tinted per vertex.
// Extended later with lighting, fog, and the alpha test when Phase 3 feeds the full
// Tessellator vertex through it. Attributes match the Tessellator's Vertex struct:
// location 0=position, 1=color, 3=normal. Location 2 (texcoord), 4 (arrayLayer) and
// 5 (worldLight) are not declared; untextured draws do not read them.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
}

@group(0) @binding(0) var<uniform> u: Uniforms;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
}

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.position = u.projectionMatrix * u.modelViewMatrix * vec4(in.position, 1.0);
    out.color = in.color;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    return in.color;
}
