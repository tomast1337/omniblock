#version 430

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec2 inUV;
layout (location = 2) in vec3 inNormal;
layout (location = 3) in uint inLocalSlot;
layout (location = 4) in uint inPartId;

out vec4 vertexColor;
out vec2 texCoord;
out float fogDistance;
// Symbolic model part (see shaders/entity_parts.properties); 0 when unmapped.
flat out uint partId;

// Matches ModelPart.MaxPartsPerModel.
const int MAX_PARTS_PER_MODEL = 16;

struct EntityInstance
{
    mat4 poseMatrices[MAX_PARTS_PER_MODEL];
    vec4 tint;
};

layout(std430, binding = 0) readonly buffer InstanceBuffer
{
    EntityInstance instances[];
};

uniform mat4 projectionMatrix;
// Start index of this draw's instances in InstanceBuffer.
uniform int instanceBase;

// Same transform the fixed-function path applies to texture coordinates. Usually identity; the
// charged creeper's glow scrolls its overlay with it.
uniform mat4 textureMatrix;

uniform bool lightingEnabled;
uniform vec3 ambient;
uniform vec3 light0Dir;
uniform vec3 light0Diffuse;
uniform vec3 light1Dir;
uniform vec3 light1Diffuse;

void main()
{
    EntityInstance inst = instances[instanceBase + gl_InstanceID];
    mat4 poseMatrix = inst.poseMatrices[inLocalSlot];

    vec4 worldPos = poseMatrix * vec4(inPosition, 1.0);
    gl_Position = projectionMatrix * worldPos;

    mat3 normalMatrix = transpose(inverse(mat3(poseMatrix)));
    vec3 normal = normalize(normalMatrix * inNormal);

    vec3 lit = lightingEnabled
        ? ambient
            + light0Diffuse * max(dot(normal, light0Dir), 0.0)
            + light1Diffuse * max(dot(normal, light1Dir), 0.0)
        : vec3(1.0);

    vec3 rgb = clamp(lit * inst.tint.rgb, 0.0, 1.0);
    float a = clamp(inst.tint.a, 0.0, 1.0);

    vertexColor = vec4(rgb, a);
    texCoord = (textureMatrix * vec4(inUV, 0.0, 1.0)).xy;
    partId = inPartId;

    fogDistance = length(worldPos.xyz);
}
