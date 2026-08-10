// gamma.frag's GL equivalent: washes the sampled colour out by the same inverse power curve, so
// the two backends' gamma sliders read the same at the same value.
struct Uniforms { gamma: f32 }

@group(0) @binding(0) var<uniform> u: Uniforms;
@group(1) @binding(0) var src: texture_2d<f32>;
@group(1) @binding(1) var smp: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) texcoord: vec2<f32>,
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) texcoord: vec2<f32>,
}

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.position = vec4<f32>(in.position, 1.0);
    out.texcoord = in.texcoord;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let col = textureSample(src, smp, in.texcoord);
    let washedOut = pow(col.rgb, vec3<f32>(1.0 / u.gamma));
    return vec4<f32>(washedOut, col.a);
}
