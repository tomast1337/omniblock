// Separable 9-tap Gaussian blur (weights below) over the captured cloud layer. Deliberately not a
// port of blur.frag's premultiplied-alpha version: that scheme's alpha-doubling restore ran on both
// the horizontal and vertical dispatch of the shared shader (its tail sits outside the pass's
// if/else), compounding to ~4x the true alpha, which past roughly a quarter alpha pushed the
// composite's (1 - alpha) blend factor negative — clamped to zero, dropping the sky out of the blend
// entirely and reading as a grey/black wash right where a cloud fades. This version samples and
// weight-averages the texture as-is in both passes, so the result's alpha is always the true
// (blurred) cloud alpha, and a plain SrcAlpha/OneMinusSrcAlpha composite fades cleanly by
// construction — no boost, no premultiply, nothing that can push a blend factor negative.
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
    // GL's post-process quad carries clip-space z=0, which under GL's [-1,1] -> [0,1] depth remap
    // lands at window depth 0.5 — a legitimate mid-range value the vertical pass's depth test
    // compares real scene depth against. WebGPU's NDC z is already [0,1] natively, so the same z=0
    // would stay literal depth 0 (nearest possible), making the depth test a no-op. Forcing 0.5 here
    // reproduces GL's effective depth regardless of what the shared quad mesh's own z happens to be.
    out.position = vec4<f32>(in.position.xy, 0.5, 1.0);
    out.texcoord = in.texcoord;
    return out;
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

    var result = textureSample(src, smp, in.texcoord) * weights[0];
    for (var i = 1; i < 5; i = i + 1) {
        let fi = f32(i);
        result += textureSample(src, smp, in.texcoord + fi * dir) * weights[i];
        result += textureSample(src, smp, in.texcoord - fi * dir) * weights[i];
    }

    return result;
}
