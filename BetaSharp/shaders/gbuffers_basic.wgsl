// The root of the fallback chain: untextured, unlit geometry, tinted per vertex.
// Attributes match the Tessellator's Vertex struct: location 0=position, 1=color.
// Locations 2 (texcoord), 3 (normal), 4 (arrayLayer) and 5 (worldLight) are not declared;
// untextured draws do not read them.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,

    // The colour a draw carrying no per-vertex colour is tinted by. OpenGL supplies this as the
    // default value of vertex attribute 1; WebGPU has no such thing, so it arrives here instead
    // and params.x says which of the two to use.
    tint: vec4<f32>,

    // x: 1.0 when the vertex buffer's colour channel was written, 0.0 when it holds nothing.
    // y: the alpha below which a fragment is discarded, or 0.0 for no test.
    params: vec4<f32>,
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
    out.color = select(u.tint, in.color * u.tint, u.params.x > 0.5);
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    if (in.color.a < u.params.y) {
        discard;
    }

    return in.color;
}
