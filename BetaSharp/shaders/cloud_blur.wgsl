// blur.frag's WGSL equivalent: same separable 9-tap Gaussian (weights below), same
// premultiply-on-horizontal / no-premultiply-on-vertical split (driven by u.horizontal instead of
// a branch on which pass this is), same alpha-doubling restore at the end to undo the premultiply's
// darkening once the vertical pass has composited over the scene.
struct Uniforms { horizontal: u32 }

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

fn sampleWeighted(uv: vec2<f32>, horizontal: bool) -> vec4<f32> {
    let s = textureSample(src, smp, uv);
    return select(s, vec4<f32>(s.rgb * s.a, s.a), horizontal);
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    // var, not let: naga only allows a runtime (non-constant) index into a function-scope array
    // when it is declared var — a let array here rejected weights[i] with "may only be indexed by
    // a constant".
    var weights = array<f32, 5>(0.2270270270, 0.1945945946, 0.1216216216, 0.0540540541, 0.0162162162);
    let horizontal = u.horizontal != 0u;
    let texSize = vec2<f32>(textureDimensions(src));
    let dir = select(vec2<f32>(0.0, 1.0 / texSize.y), vec2<f32>(1.0 / texSize.x, 0.0), horizontal);

    var result = sampleWeighted(in.texcoord, horizontal) * weights[0];
    for (var i = 1; i < 5; i = i + 1) {
        let fi = f32(i);
        result += sampleWeighted(in.texcoord + fi * dir, horizontal) * weights[i];
        result += sampleWeighted(in.texcoord - fi * dir, horizontal) * weights[i];
    }

    result.a *= 2.0;
    return result;
}
