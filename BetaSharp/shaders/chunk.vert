#version 410

layout (location = 0) in vec3 inPosition;
layout (location = 1) in uvec2 inUV;
layout (location = 2) in vec4 inColor;
layout (location = 3) in uvec2 inLight;

out vec4 vertexColor;
out vec2 texCoord;
out float fogDistance;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;
uniform vec2 chunkPos;
uniform vec3 time;

// How far the sky channel is knocked down right now, in levels. A uniform rather than something
// baked into the mesh, which is the point of keeping the channels apart.
uniform float ambientDarkness;

// The floor of the brightness curve: 0.05 in the overworld, 0.1 in the nether.
uniform float luminanceOffset;

const float WavyLeavesStrength = 1.0; // [0.0 - 4.0]
const float WavyLeavesSpeed = 1.0; // [0.1 - 2.0]
const float WavyPlantStrength = 1.0; // [0.0 - 4.0]
const float WavyPlantSpeed = 1.0; // [0.1 - 2.0]

const float WavyPlantMode = 0; // [0 1]

const float Wavy = WavyLeavesStrength + WavyPlantStrength;

const float POSITION_SCALE_INV = 64.0 / 32767.0;

vec3 unpackPosition(vec3 packedPos)
{
    return packedPos * POSITION_SCALE_INV;
}

// Quarter levels, so a smooth-lit corner's mean of four cells survives the trip exactly.
float lightLevel(uint quarterLevels)
{
    return float(quarterLevels) * 0.25;
}

// Beta's brightness curve, which used to be a 16-entry table built per dimension. It is a closed
// form, so the fractional levels smooth lighting produces can be evaluated directly rather than
// interpolated between table entries. The offset is the only part that differs by dimension.
float rampLuminance(float level)
{
    float factor = 1.0 - level / 15.0;
    return (1.0 - factor) / (factor * 3.0 + 1.0) * (1.0 - luminanceOffset) + luminanceOffset;
}

// What Chunk.GetLight used to return before the mesh builder ever saw it. Doing it here is what
// lets the sun set without every chunk in view being rebuilt, and what leaves a pack something to
// override: by this point sky and block are still telling apart.
float terrainBrightness(uvec2 packedLight)
{
    float sky = lightLevel(packedLight.x) - ambientDarkness;
    float block = lightLevel(packedLight.y);

    return rampLuminance(clamp(max(sky, block), 0.0, 15.0));
}

int atlasIndexFromUV(vec2 uv)
{
    uv = clamp(uv, 0.0, 0.999999);

    ivec2 tile = ivec2(floor(uv * 16.0));

    return tile.x + tile.y * 16;
}

vec2 localUV(uvec2 inuv)
{
    return vec2(inuv & 0xFu) / 14.0;
}

const vec2 WindDir = vec2(-0.8, 0.6); // unit vector, 0.8^2 + 0.6^2 = 1.0

vec2 calcWave(in vec3 pos)
{
    float t = 0.2 * WavyPlantSpeed * time.z;

    // Traveling phase along the wind direction so distant plants peak later than near ones.
    float phase = dot(pos.xz, WindDir) * 0.15 - t * 7.5;

    // Slow-moving gust envelope, also traveling with the wind, for bursts of stronger sway.
    float gust = 0.6 + 0.4 * sin(dot(pos.xz, WindDir) * 0.015 - t);

    // Primary sway plus a faster, smaller ripple for organic irregularity.
    float sway = sin(phase) + sin(phase * 2.3 + pos.y * 0.5) * 0.3;

    return WindDir * sway * gust * 0.02;
}

vec2 calcDynamicWind(in vec3 pos)
{
    const float f1 = 0.02;
    const float f2 = 0.05;

    float t = time.z * WavyPlantSpeed;

    float angleOffset = sin(pos.x * f1 - t * 0.5) * cos(pos.z * f1 + t * 0.3)
                      + sin(pos.x * f2 + t * 1.2) * cos(pos.z * f2 - t * 0.8);

    const float baseAngle = atan(WindDir.y, WindDir.x);
    float finalAngle = baseAngle + angleOffset * 1.5;

    vec2 dynamicDir = vec2(cos(finalAngle), sin(finalAngle));
    float gust = 0.6 + 0.4 * sin(pos.x * 0.015 - t + pos.z * 0.01);

    return dynamicDir * gust;
}

vec3 calcMovePlants(in vec3 pos)
{
    if (WavyPlantMode == 0) {
        vec2 move1 = calcWave(pos);
        float move1y = length(move1);
        move1y *= move1y * 10;
        return 5 * WavyPlantStrength * vec3(move1.x, -move1y, move1.y);
    } else {
        vec2 move1 = calcDynamicWind(pos);
        float move1y = -length(move1) * 0.5;
        return 0.1 * WavyPlantStrength * vec3(move1.x, move1y, move1.y);
    }
}

vec3 calcWaveLeaves(in vec3 pos)
{
    float pi2wt = 2.0 * 3.14159265 * WavyLeavesSpeed * time.z;
    float magnitude = abs(sin(dot(vec4(WavyLeavesSpeed * time.z, pos), vec4(1.0, 0.005, 0.005, 0.005))) * 0.5 + 0.72) * 0.013;
    vec3 ret = sin(pi2wt * vec3(0.0063, 0.0224, 0.0015) * 1.5 - pos) * magnitude;
    return ret;
}

vec3 calcMoveLeaves(in vec3 pos)
{
    vec3 move1 = calcWaveLeaves(pos) * vec3(1.0, 0.2, 1.0);
    return 5.0 * WavyLeavesStrength * move1;
}

bool isLeaf(int idx)
{
    return idx == 52 || idx == 132;
}

bool isPlant(int idx)
{
    return idx == 12 || idx == 13 || idx == 39 || idx == 55 || idx == 56;
}

void main()
{
    vec3 position = unpackPosition(inPosition);
    vec2 uv = vec2(inUV & 0x7FFFu) / 32767.0;
    uvec2 signBits = (inUV >> 15u) & 1u;

    const float epsilon = 1.0 / 65536.0;
    vec2 bias = vec2(
    (signBits.x == 0u) ? epsilon : -epsilon,
    (signBits.y == 0u) ? epsilon : -epsilon
    );

    uv += bias;

    if (Wavy > 0)
    {
        int textureIndex = atlasIndexFromUV(uv);

        vec3 worldPos = position + vec3(chunkPos.x, 0.0, chunkPos.y);

        if (WavyLeavesStrength > 0.0 && isLeaf(textureIndex))
        {
            worldPos += calcMoveLeaves(worldPos);
        }
        else if (WavyPlantStrength > 0.0 && isPlant(textureIndex))
        {
            vec2 luv = localUV(inUV);
            worldPos += calcMovePlants(worldPos) * (1.0 - luv.y);
        }

        position = worldPos - vec3(chunkPos.x, 0.0, chunkPos.y);
    }

    // The vertex colour is the block's own colour and its face shading; how lit it is arrives
    // separately and is applied here rather than baked in by the mesh builder.
    vec4 color = vec4(inColor.rgb * terrainBrightness(inLight), inColor.a);

    vec4 viewPos = modelViewMatrix * vec4(position, 1.0);
    gl_Position = projectionMatrix * viewPos;

    vertexColor = color;
    texCoord = uv;

    fogDistance = length(viewPos.xyz);
}
