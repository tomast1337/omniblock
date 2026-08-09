// Cloud layer, parallax-rendered for depth regardless of the quality preset.
// Geometry from the Tessellator seam — same vertex layout as sky.wgsl.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
    textureMatrix: mat4x4<f32>,
    cloudOffset: vec3<f32>,
    // 4 bytes padding — vec3 takes 16 bytes
    cloudScale: f32,
    fogStart: f32,
    fogEnd: f32,
    // 4 bytes padding to reach the next 16-byte boundary
    tint: vec4<f32>,
}

@group(0) @binding(0) var<uniform> u: Uniforms;

@group(1) @binding(0) var t_clouds: texture_2d<f32>;
@group(1) @binding(1) var s_clouds: sampler;

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
}

// Constants from the GLSL preset lines, in the order they appear there.
// Since these are compile-time constants they cannot be driven by uniforms;
// the hard-coded values below match the medium-quality preset.
const cloudHeight: f32 = 4.0;
const texSize: i32 = 256;
const scaledTextSize: f32 = cloudHeight / f32(texSize);
const shadow: f32 = 0.66;
const minGradient: i32 = 0;
const maxGradient: i32 = 10;
const smoothWidth: f32 = 0.5;
const gradientWidth: f32 = smoothWidth / f32(texSize);
const smoothSamples: i32 = 4;
const closeFade: f32 = 8.0;
const smoothTop: i32 = 0;
const smoothEdge: i32 = 1;
const smoothBottom: i32 = 0;
const smoothBottomTopDown: i32 = 0;

const minParallax: i32 = 4;
const maxParallax: i32 = 32;

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.position = u.projectionMatrix * u.modelViewMatrix * vec4(in.position, 1.0);
    out.color = in.color;
    out.texcoord = (u.textureMatrix * vec4(in.texcoord, 0.0, 1.0)).xy;
    out.localPos = in.position;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let normal = in.localPos + u.cloudOffset;

    let dist = length(normal.xz) * u.cloudScale;
    let fogFactor = clamp((u.fogEnd - dist) / (u.fogEnd - u.fogStart), 0.0, 1.0);

    var color = in.color * u.tint;
    var len: f32;

    if (closeFade > 0.0) {
        len = length(normal);
        color.a *= clamp(len / closeFade - closeFade / 8.0, 0.0, 1.0);
    }

    if (color.a * fogFactor < 0.001) {
        discard;
    }

    // --- Parallax rendering ---
    // No parallax path (minParallax==0 && maxParallax==0) is unreachable here:
    // the configurable constants are always > 0 in practice.

    // Sample the base texel first.
    if (closeFade <= 0.0) {
        len = length(normal);
    }

    var texColor = textureSample(t_clouds, s_clouds, in.texcoord);
    if (len < 1.0) { len = 1.0; }

    // Parallax count decreases with fog distance (the far clouds are flatter).
    var parallaxCount: i32;
    if (maxParallax <= minParallax) {
        parallaxCount = minParallax;
    } else {
        let fogSq = fogFactor * fogFactor;
        parallaxCount = i32(fogSq * f32(maxParallax - minParallax)) + minParallax;
    }
    let parallaxStep = len / (scaledTextSize / f32(parallaxCount));

    if (texColor.a < 0.1) {
        // The base texel is empty — walk "through" the cloud along the view ray.

        // When seen from above, flip the expand direction.
        var searchDir = normal;
        if (normal.y < 0.0) { searchDir = -searchDir; }
        let texOff = searchDir.xz / parallaxStep;

        if (smoothEdge <= 0) {
            // Binary: first non-empty texel wins.
            for (var i = parallaxCount; i > 0; i--) {
                texColor = textureSample(t_clouds, s_clouds, in.texcoord + texOff * f32(i));
                if (texColor.a >= 0.1) { break; }
            }
        } else if (smoothTop <= 0) {
            // Edge smoothing without top smoothing.
            texColor = textureSample(t_clouds, s_clouds, in.texcoord + texOff * f32(parallaxCount));
            if (texColor.a < 0.1) {
                var dens: i32 = 0;
                for (var i = parallaxCount - 1; i > 0; i--) {
                    let c = textureSample(t_clouds, s_clouds, in.texcoord + texOff * f32(i));
                    if (c.a >= 0.1) {
                        texColor = c;
                        dens++;
                        if (dens >= smoothSamples) { break; }
                    }
                }
                texColor.a *= f32(dens) / f32(smoothSamples);
            }
        } else {
            // Full smoothing (top + edge).
            var dens: i32 = 0;
            for (var i = parallaxCount; i > 0; i--) {
                let c = textureSample(t_clouds, s_clouds, in.texcoord + texOff * f32(i));
                if (c.a >= 0.1) {
                    texColor = c;
                    dens++;
                    if (dens >= smoothSamples) { break; }
                }
            }
            texColor.a *= f32(dens) / f32(smoothSamples);
        }

        if (texColor.a < 0.1) { discard; }
    } else if (normal.y >= 0.0) {
        // A texel on the top side: apply the gradient shadow on its underside.

        var gradientCount: i32;
        if (maxGradient <= minGradient) {
            gradientCount = minGradient;
        } else {
            let fogSq = fogFactor * fogFactor;
            gradientCount = i32(fogSq * f32(maxGradient - minGradient)) + minGradient;
        }

        if (gradientCount > 0) {
            let stepSize = gradientWidth / f32(gradientCount);
            let gradOff = normal.xz / (len / (gradientWidth / f32(gradientCount)));

            var gradiant: f32 = 0.0;
            for (var i = gradientCount; i > 0; i--) {
                let probe = textureSample(t_clouds, s_clouds, in.texcoord - (gradOff * f32(i)));
                if (probe.a < 0.1) { gradiant += 1.0; }
            }

            gradiant /= f32(gradientCount);
            texColor = vec4(texColor.rgb * (gradiant + shadow * (1.0 - gradiant)), texColor.a);

            // Smooth the bottom edge under this top-facing texel.
            if (smoothBottom > 0) {
                var dens: i32 = 0;
                let botOff = normal.xz / (len / (scaledTextSize / parallaxStep));
                for (var i = smoothSamples; i > 0; i--) {
                    let probe = textureSample(t_clouds, s_clouds,
                        in.texcoord + botOff * f32(i));
                    if (probe.a >= 0.1) {
                        dens++;
                        if (dens >= smoothSamples) { break; }
                    }
                }
                texColor.a *= f32(dens) / f32(smoothSamples);
            }
        }
    } else if (smoothBottomTopDown > 0) {
        // Bottom side smoothing.
        var dens: i32 = 0;
        let texOff = normal.xz / parallaxStep;
        for (var i = smoothSamples; i > 0; i--) {
            let probe = textureSample(t_clouds, s_clouds, in.texcoord - texOff * f32(i));
            if (probe.a >= 0.1) {
                dens++;
                if (dens >= smoothSamples) { break; }
            }
        }
        texColor.a *= f32(dens) / f32(smoothSamples);
    }

    var result = texColor * color;
    result.a *= fogFactor;
    return result;
}
