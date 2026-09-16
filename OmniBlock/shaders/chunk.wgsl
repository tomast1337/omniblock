// Terrain rendered from chunk meshes — the WGSL equivalent of chunk.{vert,frag}.
// Geometry and light are separate vertex streams. The stable 20-byte ChunkVertex carries packed
// position/UV/colour/layer; a replaceable 4-byte ChunkLightVertex carries sky and block light.
// A propagated-light update can therefore replace lighting without touching geometry.

struct DrawMetadata {
    modelViewMatrix: mat4x4<f32>,
    chunkPos: vec2<f32>,       // chunk X,Z in world space, for wavy animation
    fadeProgress: f32,
    chunkFadeEnabled: u32,     // bool as u32
    presentationFadeMode: u32, // 0=none, 1=near fade-in, 2=LOD fade-out
    presentationFadeSeed: u32,
};

struct FrameUniforms {
    projectionMatrix: mat4x4<f32>,
    time: vec3<f32>,           // total seconds, for wavy animation
    ambientDarkness: f32,      // how far the sky channel is knocked down
    luminanceOffset: f32,      // floor of the brightness curve (0.05 overworld, 0.1 nether)
    wavyLeavesStrength: f32,   // 0–4, 0 when still
    wavyLeavesSpeed: f32,      // 0.1–2
    wavyPlantStrength: f32,    // 0–4
    wavyPlantSpeed: f32,       // 0.1–2
    wavyPlantMode: u32,        // 0 or 1, picks the sway algorithm
    // 8 u32 values packed into 2 vec4s; each .x/.y/.z/.w is one layer.
    wavyLeafLayers0: vec4<u32>,
    wavyLeafLayers1: vec4<u32>,
    wavyLeafCount: u32,
    wavyPlantLayers0: vec4<u32>,
    wavyPlantLayers1: vec4<u32>,
    wavyPlantCount: u32,
    fogColor: vec4<f32>,
    fogStart: f32,
    fogEnd: f32,
    fogDensity: f32,
    fogMode: u32,              // 0=linear, else=exponential
};

@group(0) @binding(0) var<uniform> frame: FrameUniforms;
@group(1) @binding(0) var terrainArray: texture_2d_array<f32>;
@group(1) @binding(1) var terrainSampler: sampler;
@group(2) @binding(0) var<storage, read> drawMetadata: array<DrawMetadata>;

const POSITION_SCALE_INV: f32 = 64.0 / 32767.0;

fn unpackPosition(packed: vec4<i32>) -> vec3<f32> {
    return vec3<f32>(f32(packed.x), f32(packed.y), f32(packed.z)) * POSITION_SCALE_INV;
}

fn rampLuminance(level: f32) -> f32 {
    let factor = 1.0 - level / 15.0;
    return (1.0 - factor) / (factor * 3.0 + 1.0) * (1.0 - frame.luminanceOffset) + frame.luminanceOffset;
}

fn terrainBrightness(packedLight: vec2<u32>) -> f32 {
    let sky = f32(packedLight.x) * 0.25 - frame.ambientDarkness;
    let block = f32(packedLight.y) * 0.25;
    return rampLuminance(clamp(max(sky, block), 0.0, 15.0));
}

// Wavy leaves — subtle oscillation driven by time and position.
const WIND_DIR: vec2<f32> = vec2<f32>(-0.8, 0.6);

fn calcWaveLeaves(pos: vec3<f32>) -> vec3<f32> {
    let pi2wt = 2.0 * 3.14159265 * frame.wavyLeavesSpeed * frame.time.z;
    let magnitude = abs(sin(dot(vec4<f32>(frame.wavyLeavesSpeed * frame.time.z, pos), vec4<f32>(1.0, 0.005, 0.005, 0.005))) * 0.5 + 0.72) * 0.013;
    return sin(pi2wt * vec3<f32>(0.0063, 0.0224, 0.0015) * 1.5 - pos) * magnitude;
}

fn calcMoveLeaves(pos: vec3<f32>) -> vec3<f32> {
    let move1 = calcWaveLeaves(pos) * vec3<f32>(1.0, 0.2, 1.0);
    return 5.0 * frame.wavyLeavesStrength * move1;
}

fn calcWave(pos: vec3<f32>) -> vec2<f32> {
    let t = 0.2 * frame.wavyPlantSpeed * frame.time.z;
    let phase = dot(pos.xz, WIND_DIR) * 0.15 - t * 7.5;
    let gust = 0.6 + 0.4 * sin(dot(pos.xz, WIND_DIR) * 0.015 - t);
    let sway = sin(phase) + sin(phase * 2.3 + pos.y * 0.5) * 0.3;
    return WIND_DIR * sway * gust * 0.02;
}

fn calcMovePlants(pos: vec3<f32>) -> vec3<f32> {
    let move1 = calcWave(pos);
    let move1y = length(move1) * length(move1) * 10.0;
    return 5.0 * frame.wavyPlantStrength * vec3<f32>(move1.x, -move1y, move1.y);
}

fn isLeaf(layer: u32) -> bool {
    if (frame.wavyLeafCount > 0u && frame.wavyLeafLayers0.x == layer) { return true; }
    if (frame.wavyLeafCount > 1u && frame.wavyLeafLayers0.y == layer) { return true; }
    if (frame.wavyLeafCount > 2u && frame.wavyLeafLayers0.z == layer) { return true; }
    if (frame.wavyLeafCount > 3u && frame.wavyLeafLayers0.w == layer) { return true; }
    if (frame.wavyLeafCount > 4u && frame.wavyLeafLayers1.x == layer) { return true; }
    if (frame.wavyLeafCount > 5u && frame.wavyLeafLayers1.y == layer) { return true; }
    if (frame.wavyLeafCount > 6u && frame.wavyLeafLayers1.z == layer) { return true; }
    if (frame.wavyLeafCount > 7u && frame.wavyLeafLayers1.w == layer) { return true; }
    return false;
}

fn isPlant(layer: u32) -> bool {
    if (frame.wavyPlantCount > 0u && frame.wavyPlantLayers0.x == layer) { return true; }
    if (frame.wavyPlantCount > 1u && frame.wavyPlantLayers0.y == layer) { return true; }
    if (frame.wavyPlantCount > 2u && frame.wavyPlantLayers0.z == layer) { return true; }
    if (frame.wavyPlantCount > 3u && frame.wavyPlantLayers0.w == layer) { return true; }
    if (frame.wavyPlantCount > 4u && frame.wavyPlantLayers1.x == layer) { return true; }
    if (frame.wavyPlantCount > 5u && frame.wavyPlantLayers1.y == layer) { return true; }
    if (frame.wavyPlantCount > 6u && frame.wavyPlantLayers1.z == layer) { return true; }
    if (frame.wavyPlantCount > 7u && frame.wavyPlantLayers1.w == layer) { return true; }
    return false;
}

struct VertexInput {
    @location(0) position: vec4<i32>,       // Sint16x4 at offset 0, w is padding
    @location(1) uv: vec2<u32>,             // Uint16x2 at offset 12
    @location(2) color: vec4<f32>,          // Unorm8x4 at offset 8
    @location(3) light: vec2<u32>,          // Uint8x2 at offset 16
    @location(4) @interpolate(flat) arrayLayer: vec2<u32>, // Uint8x2 at offset 18, .x is the layer
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) texCoord: vec2<f32>,
    @location(2) @interpolate(flat) arrayLayer: i32,
    @location(3) fogDistance: f32,
    @location(4) @interpolate(flat) fadeProgress: f32,
    @location(5) @interpolate(flat) chunkFadeEnabled: u32,
    @location(6) @interpolate(flat) presentationFadeMode: u32,
    @location(7) @interpolate(flat) presentationFadeSeed: u32,
}

@vertex
fn vs_main(in: VertexInput, @builtin(instance_index) drawIndex: u32) -> VertexOutput {
    let draw = drawMetadata[drawIndex];
    var pos = unpackPosition(in.position);

    // UV: ushort range, the full 0–65535 maps to 0.0–16.0 — a sub-chunk's width, the widest a
    // greedy-merged quad can tile across. Must match Tessellator.UV_SCALE exactly (encode/decode).
    let uv = vec2<f32>(f32(in.uv.x), f32(in.uv.y)) / 4095.0;
    let layer = in.arrayLayer.x;
    let wavy = frame.wavyLeavesStrength + frame.wavyPlantStrength;

    if (wavy > 0.0) {
        var worldPos = pos + vec3<f32>(draw.chunkPos.x, 0.0, draw.chunkPos.y);

        if (frame.wavyLeavesStrength > 0.0 && isLeaf(layer)) {
            worldPos += calcMoveLeaves(worldPos);
        } else if (frame.wavyPlantStrength > 0.0 && isPlant(layer)) {
            worldPos += calcMovePlants(worldPos) * (1.0 - uv.y);
        }

        pos = worldPos - vec3<f32>(draw.chunkPos.x, 0.0, draw.chunkPos.y);
    }

    let color = vec4<f32>(in.color.rgb * terrainBrightness(in.light), in.color.a);
    let viewPos = draw.modelViewMatrix * vec4<f32>(pos, 1.0);

    var out: VertexOutput;
    out.position = frame.projectionMatrix * viewPos;
    out.color = color;
    out.texCoord = uv;
    out.arrayLayer = i32(layer);
    out.fogDistance = length(viewPos.xyz);
    out.fadeProgress = draw.fadeProgress;
    out.chunkFadeEnabled = draw.chunkFadeEnabled;
    out.presentationFadeMode = draw.presentationFadeMode;
    out.presentationFadeSeed = draw.presentationFadeSeed;
    return out;
}

// Debug wireframe overlay: same vertex stage as fs_main, but flat-shaded and untextured so the
// mesh's actual triangle edges — including the diagonal each quad was split along — are legible
// regardless of what's under them.
@fragment
fn fs_wireframe(in: VertexOutput) -> @location(0) vec4<f32> {
    return vec4<f32>(0.0, 1.0, 0.0, 1.0);
}

fn presentationDitherThreshold(position: vec2<f32>, seed: u32) -> f32 {
    let x = (u32(position.x) + seed) & 3u;
    let y = (u32(position.y) + (seed >> 2u)) & 3u;
    var value = 0u;
    if (y == 0u) {
        if (x == 0u) { value = 0u; }
        else if (x == 1u) { value = 8u; }
        else if (x == 2u) { value = 2u; }
        else { value = 10u; }
    } else if (y == 1u) {
        if (x == 0u) { value = 12u; }
        else if (x == 1u) { value = 4u; }
        else if (x == 2u) { value = 14u; }
        else { value = 6u; }
    } else if (y == 2u) {
        if (x == 0u) { value = 3u; }
        else if (x == 1u) { value = 11u; }
        else if (x == 2u) { value = 1u; }
        else { value = 9u; }
    } else {
        if (x == 0u) { value = 15u; }
        else if (x == 1u) { value = 7u; }
        else if (x == 2u) { value = 13u; }
        else { value = 5u; }
    }
    return (f32(value) + 0.5) / 16.0;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let texColor = textureSample(terrainArray, terrainSampler, in.texCoord, in.arrayLayer);
    var finalColor = texColor * in.color;

    if (finalColor.a < 0.001) {
        discard;
    }

    if (in.presentationFadeMode != 0u) {
        let threshold = presentationDitherThreshold(in.position.xy, in.presentationFadeSeed);
        if (in.presentationFadeMode == 1u && threshold >= in.fadeProgress) {
            discard;
        }
        if (in.presentationFadeMode == 2u && threshold < in.fadeProgress) {
            discard;
        }
    }

    var fogFactor: f32;
    if (frame.fogMode == 0u) {
        fogFactor = (frame.fogEnd - in.fogDistance) / (frame.fogEnd - frame.fogStart);
    } else {
        fogFactor = exp(-frame.fogDensity * in.fogDistance);
    }
    fogFactor = clamp(fogFactor, 0.0, 1.0);

    var fogApplied = mix(frame.fogColor, finalColor, vec4<f32>(fogFactor));

    if (in.chunkFadeEnabled != 0u) {
        finalColor = mix(frame.fogColor, fogApplied, vec4<f32>(in.fadeProgress));
    } else {
        finalColor = fogApplied;
    }

    return finalColor;
}
