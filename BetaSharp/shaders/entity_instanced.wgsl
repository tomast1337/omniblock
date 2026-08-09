// GPU-instanced entity models: one static vertex buffer shared by every model (each model a
// contiguous range within it), posed per instance by a matrix pulled from a storage buffer indexed
// by @builtin(instance_index). Mirrors shaders/entity_instanced.vert/.frag's GLSL semantics.

struct Uniforms {
    projectionMatrix: mat4x4<f32>,
    // Same transform the fixed-function path applies to texture coordinates. Usually identity; the
    // charged creeper's glow scrolls its overlay with it.
    textureMatrix: mat4x4<f32>,
    ambient: vec3<f32>,
    lightingEnabled: u32,
    light0Dir: vec3<f32>,
    useTexture: u32,
    light0Diffuse: vec3<f32>,
    alphaThreshold: f32,
    light1Dir: vec3<f32>,
    fogEnabled: u32,
    light1Diffuse: vec3<f32>,
    fogMode: i32,
    fogColor: vec4<f32>,
    fogStart: f32,
    fogEnd: f32,
    fogDensity: f32,
}

@group(0) @binding(0) var<uniform> u: Uniforms;

struct EntityInstance {
    // 16 matches ModelPart.MaxPartsPerModel. Inlined rather than a named const: naga's WGSL front
    // end wants a literal in an array-size template argument, not an identifier.
    poseMatrices: array<mat4x4<f32>, 16>,
    tint: vec4<f32>,
}

// Bound as the pipeline's "texture" group (group 1) even though it carries a storage buffer — see
// WgpuParticleRenderer for the same repurposing and why.
@group(1) @binding(0) var<storage, read> instances: array<EntityInstance>;

@group(2) @binding(0) var t: texture_2d<f32>;
@group(2) @binding(1) var s: sampler;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) normal: vec3<f32>,
    @location(3) localSlot: u32,
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texcoord: vec2<f32>,
    @location(2) fogDistance: f32,
}

@vertex
fn vs_main(in: VertexInput, @builtin(instance_index) instanceIndex: u32) -> VertexOutput {
    // Indexed directly off the storage-buffer expression rather than through a `let`-bound copy:
    // naga only allows dynamic indexing on a pointer chain into memory, not on a value already
    // materialized into a local variable (see particle.wgsl's CORNER_SIGN for the same trap).
    let poseMatrix = instances[instanceIndex].poseMatrices[in.localSlot];
    let tint = instances[instanceIndex].tint;

    let worldPos = poseMatrix * vec4<f32>(in.position, 1.0);

    var out: VertexOutput;
    out.position = u.projectionMatrix * worldPos;

    let normalMatrix = mat3x3<f32>(poseMatrix[0].xyz, poseMatrix[1].xyz, poseMatrix[2].xyz);
    let normal = normalize(normalMatrix * in.normal);

    var lit = vec3<f32>(1.0, 1.0, 1.0);
    if (u.lightingEnabled != 0u) {
        lit = u.ambient
            + u.light0Diffuse * max(dot(normal, u.light0Dir), 0.0)
            + u.light1Diffuse * max(dot(normal, u.light1Dir), 0.0);
    }

    let rgb = clamp(lit * tint.rgb, vec3<f32>(0.0), vec3<f32>(1.0));
    let a = clamp(tint.a, 0.0, 1.0);

    out.color = vec4<f32>(rgb, a);
    out.texcoord = (u.textureMatrix * vec4<f32>(in.uv, 0.0, 1.0)).xy;
    out.fogDistance = length(worldPos.xyz);
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let texColor = select(vec4<f32>(1.0), textureSample(t, s, in.texcoord), u.useTexture != 0u);
    let finalColor = texColor * in.color;

    if (finalColor.a < u.alphaThreshold) {
        discard;
    }

    if (u.fogEnabled == 0u) {
        return finalColor;
    }

    var fogFactor: f32;
    if (u.fogMode == 0) {
        fogFactor = (u.fogEnd - in.fogDistance) / (u.fogEnd - u.fogStart);
    } else {
        fogFactor = exp(-u.fogDensity * in.fogDistance);
    }
    fogFactor = clamp(fogFactor, 0.0, 1.0);

    return mix(u.fogColor, finalColor, fogFactor);
}
