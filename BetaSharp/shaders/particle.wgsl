// Billboarded particles, geometry-instanced: the CPU hands over one small record per particle and
// the corners of its quad are built here, instead of a Tessellator batch expanding four vertices
// per particle on the CPU every frame.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
    // The camera's right and up axes, in the same rotation-only view space the particle positions
    // below are already relative to. Constant for every particle in a layer, so they live in the
    // uniform block rather than the per-instance one.
    right: vec3<f32>,
    up: vec3<f32>,
}

@group(0) @binding(0) var<uniform> u: Uniforms;

// One record per particle, written by the CPU once a tick's worth of physics has run. Read-only
// here — nothing on the GPU side ever mutates a particle.
struct Particle {
    // Camera-relative, like everything else the world renders in float: the CPU subtracts the
    // camera's double-precision position before this ever reaches a shader, which is what keeps a
    // particle at a large world coordinate from jittering.
    pos: vec3<f32>,
    size: f32,
    // Already includes the world-brightness term ComputeBrightness works out per particle; the
    // shader only ever multiplies it by the sampled texel.
    color: vec4<f32>,
    uvMin: vec2<f32>,
    uvMax: vec2<f32>,
}

@group(1) @binding(0) var<storage, read> particles: array<Particle>;

@group(2) @binding(0) var t: texture_2d<f32>;
@group(2) @binding(1) var s: sampler;

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texcoord: vec2<f32>,
}

@vertex
fn vs_main(@builtin(vertex_index) vertexIndex: u32, @builtin(instance_index) instanceIndex: u32) -> VertexOutput {
    // Corner order matches what ParticleRenderer.Render used to hand the Tessellator: (-right,-up),
    // (-right,+up), (+right,+up), (+right,-up), wound as two triangles rather than a quad primitive
    // WebGPU does not have. Declared as function-local `let` rather than module-scope `const`: naga
    // rejects dynamically indexing a module-scope const array with a runtime value.
    // `var`, not `let`: dynamic (non-constant) array indexing needs a pointer to backing storage,
    // which only a variable provides — a `let`-bound array is a value, and naga rejects indexing
    // it with anything but a constant.
    var CORNER_SIGN = array<vec2<f32>, 4>(
        vec2<f32>(-1.0, -1.0),
        vec2<f32>(-1.0, 1.0),
        vec2<f32>(1.0, 1.0),
        vec2<f32>(1.0, -1.0),
    );
    var TRI_CORNER = array<u32, 6>(0u, 1u, 2u, 0u, 2u, 3u);

    let p = particles[instanceIndex];
    let corner = TRI_CORNER[vertexIndex % 6u];
    let cornerSign = CORNER_SIGN[corner];

    let worldPos = p.pos + u.right * (cornerSign.x * p.size) + u.up * (cornerSign.y * p.size);

    var out: VertexOutput;
    out.position = u.projectionMatrix * u.modelViewMatrix * vec4<f32>(worldPos, 1.0);
    out.color = p.color;
    out.texcoord = vec2<f32>(
        select(p.uvMin.x, p.uvMax.x, cornerSign.x < 0.0),
        select(p.uvMin.y, p.uvMax.y, cornerSign.y < 0.0));
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let sampled = textureSample(t, s, in.texcoord);
    if (sampled.a < 0.1) {
        discard;
    }

    return in.color * sampled;
}
