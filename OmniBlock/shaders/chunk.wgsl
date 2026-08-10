// Terrain rendered from chunk meshes — the WGSL equivalent of chunk.{vert,frag}.
// ChunkVertex: packed shorts for position and UVs, RGBA8 colour, two u8 light channels, and the
// texture-array layer as a ushort. Position is Sint16x4 (w padded, 20-byte stride) because WGSL
// has no Sint16x3 vertex format.

struct Uniforms {
    modelViewMatrix: mat4x4<f32>,
    projectionMatrix: mat4x4<f32>,
    chunkPos: vec2<f32>,       // chunk X,Z in world space, for wavy animation
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
    chunkFadeEnabled: u32,     // bool as u32
    fadeProgress: f32,
};

@group(0) @binding(0) var<uniform> u: Uniforms;
@group(1) @binding(0) var terrainArray: texture_2d_array<f32>;
@group(1) @binding(1) var terrainSampler: sampler;

const POSITION_SCALE_INV: f32 = 64.0 / 32767.0;

fn unpackPosition(packed: vec4<i32>) -> vec3<f32> {
    return vec3<f32>(f32(packed.x), f32(packed.y), f32(packed.z)) * POSITION_SCALE_INV;
}

fn rampLuminance(level: f32) -> f32 {
    let factor = 1.0 - level / 15.0;
    return (1.0 - factor) / (factor * 3.0 + 1.0) * (1.0 - u.luminanceOffset) + u.luminanceOffset;
}

fn terrainBrightness(packedLight: vec2<u32>) -> f32 {
    let sky = f32(packedLight.x) * 0.25 - u.ambientDarkness;
    let block = f32(packedLight.y) * 0.25;
    return rampLuminance(clamp(max(sky, block), 0.0, 15.0));
}

// Wavy leaves — subtle oscillation driven by time and position.
const WIND_DIR: vec2<f32> = vec2<f32>(-0.8, 0.6);

fn calcWaveLeaves(pos: vec3<f32>) -> vec3<f32> {
    let pi2wt = 2.0 * 3.14159265 * u.wavyLeavesSpeed * u.time.z;
    let magnitude = abs(sin(dot(vec4<f32>(u.wavyLeavesSpeed * u.time.z, pos), vec4<f32>(1.0, 0.005, 0.005, 0.005))) * 0.5 + 0.72) * 0.013;
    return sin(pi2wt * vec3<f32>(0.0063, 0.0224, 0.0015) * 1.5 - pos) * magnitude;
}

fn calcMoveLeaves(pos: vec3<f32>) -> vec3<f32> {
    let move1 = calcWaveLeaves(pos) * vec3<f32>(1.0, 0.2, 1.0);
    return 5.0 * u.wavyLeavesStrength * move1;
}

fn calcWave(pos: vec3<f32>) -> vec2<f32> {
    let t = 0.2 * u.wavyPlantSpeed * u.time.z;
    let phase = dot(pos.xz, WIND_DIR) * 0.15 - t * 7.5;
    let gust = 0.6 + 0.4 * sin(dot(pos.xz, WIND_DIR) * 0.015 - t);
    let sway = sin(phase) + sin(phase * 2.3 + pos.y * 0.5) * 0.3;
    return WIND_DIR * sway * gust * 0.02;
}

fn calcMovePlants(pos: vec3<f32>) -> vec3<f32> {
    let move1 = calcWave(pos);
    let move1y = length(move1) * length(move1) * 10.0;
    return 5.0 * u.wavyPlantStrength * vec3<f32>(move1.x, -move1y, move1.y);
}

fn isLeaf(layer: u32) -> bool {
    if (u.wavyLeafCount > 0u && u.wavyLeafLayers0.x == layer) { return true; }
    if (u.wavyLeafCount > 1u && u.wavyLeafLayers0.y == layer) { return true; }
    if (u.wavyLeafCount > 2u && u.wavyLeafLayers0.z == layer) { return true; }
    if (u.wavyLeafCount > 3u && u.wavyLeafLayers0.w == layer) { return true; }
    if (u.wavyLeafCount > 4u && u.wavyLeafLayers1.x == layer) { return true; }
    if (u.wavyLeafCount > 5u && u.wavyLeafLayers1.y == layer) { return true; }
    if (u.wavyLeafCount > 6u && u.wavyLeafLayers1.z == layer) { return true; }
    if (u.wavyLeafCount > 7u && u.wavyLeafLayers1.w == layer) { return true; }
    return false;
}

fn isPlant(layer: u32) -> bool {
    if (u.wavyPlantCount > 0u && u.wavyPlantLayers0.x == layer) { return true; }
    if (u.wavyPlantCount > 1u && u.wavyPlantLayers0.y == layer) { return true; }
    if (u.wavyPlantCount > 2u && u.wavyPlantLayers0.z == layer) { return true; }
    if (u.wavyPlantCount > 3u && u.wavyPlantLayers0.w == layer) { return true; }
    if (u.wavyPlantCount > 4u && u.wavyPlantLayers1.x == layer) { return true; }
    if (u.wavyPlantCount > 5u && u.wavyPlantLayers1.y == layer) { return true; }
    if (u.wavyPlantCount > 6u && u.wavyPlantLayers1.z == layer) { return true; }
    if (u.wavyPlantCount > 7u && u.wavyPlantLayers1.w == layer) { return true; }
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
}

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var pos = unpackPosition(in.position);

    // UV: ushort range, the full 0–65535 maps to 0.0–2.0 for flowing-water wrap.
    let uv = vec2<f32>(f32(in.uv.x), f32(in.uv.y)) / 32767.0;
    let layer = in.arrayLayer.x;
    let wavy = u.wavyLeavesStrength + u.wavyPlantStrength;

    if (wavy > 0.0) {
        var worldPos = pos + vec3<f32>(u.chunkPos.x, 0.0, u.chunkPos.y);

        if (u.wavyLeavesStrength > 0.0 && isLeaf(layer)) {
            worldPos += calcMoveLeaves(worldPos);
        } else if (u.wavyPlantStrength > 0.0 && isPlant(layer)) {
            worldPos += calcMovePlants(worldPos) * (1.0 - uv.y);
        }

        pos = worldPos - vec3<f32>(u.chunkPos.x, 0.0, u.chunkPos.y);
    }

    let color = vec4<f32>(in.color.rgb * terrainBrightness(in.light), in.color.a);
    let viewPos = u.modelViewMatrix * vec4<f32>(pos, 1.0);

    var out: VertexOutput;
    out.position = u.projectionMatrix * viewPos;
    out.color = color;
    out.texCoord = uv;
    out.arrayLayer = i32(layer);
    out.fogDistance = length(viewPos.xyz);
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let texColor = textureSample(terrainArray, terrainSampler, in.texCoord, in.arrayLayer);
    var finalColor = texColor * in.color;

    if (finalColor.a < 0.001) {
        discard;
    }

    var fogFactor: f32;
    if (u.fogMode == 0u) {
        fogFactor = (u.fogEnd - in.fogDistance) / (u.fogEnd - u.fogStart);
    } else {
        fogFactor = exp(-u.fogDensity * in.fogDistance);
    }
    fogFactor = clamp(fogFactor, 0.0, 1.0);

    var fogApplied = mix(u.fogColor, finalColor, vec4<f32>(fogFactor));

    if (u.chunkFadeEnabled != 0u) {
        finalColor = mix(u.fogColor, fogApplied, vec4<f32>(u.fadeProgress));
    } else {
        finalColor = fogApplied;
    }

    return finalColor;
}
