#version 330 core

// Textured, tinted, and shaded by the two directional lights. Attributes 1 and 3 are read whether or
// not the Tessellator bound arrays to them — geometry with one colour or one facing throughout
// supplies no array and the value arrives as the attribute's default.

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec4 inColor;
layout (location = 2) in vec2 inUV;
layout (location = 3) in vec3 inNormal;

// Which layer of the named texture array this vertex samples, or -1 for a draw that means the
// plain 2D texture on unit 0. Carried per vertex rather than switched by a uniform so a batch can
// mix the two -- an item icon and the block behind it go out together.
layout (location = 4) in int inArrayLayer;

// The two world-light channels in quarter levels. Full block light for a draw that sets none, which
// is text, the GUI, and anything else that is not a block sitting in the world.
layout (location = 5) in uvec2 inWorldLight;

// How far the sky channel is knocked down right now, and the floor of the brightness curve. The
// same two the terrain shader takes, because this applies the same ramp to the same levels: a block
// drawn from the Tessellator has to come out lit like the one meshed into a chunk beside it.
uniform float ambientDarkness;
uniform float luminanceOffset;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;
uniform mat4 textureMatrix;
uniform mat3 normalMatrix;

// Not folded into the choice of slot. A draw belongs here because it *may* be lit, and several of
// them — particles, weather — switch lighting off for a pass and back on without changing what they
// are. Making the slot mean "lit unconditionally" would put those on the wrong side of the split.
uniform int lightingEnabled;
uniform vec3 light0Direction;
uniform vec3 light0Diffuse;
uniform vec3 light1Direction;
uniform vec3 light1Diffuse;
uniform vec3 ambientLight;

flat out vec4 vertexColorFlat;
out vec4 vertexColorSmooth;
out vec2 texCoord;
out float fogDistance;
flat out int arrayLayer;

// Beta's brightness curve in closed form, matching chunk.vert -- see the longer note there.
float rampLuminance(float level)
{
    float factor = 1.0 - level / 15.0;
    return (1.0 - factor) / (factor * 3.0 + 1.0) * (1.0 - luminanceOffset) + luminanceOffset;
}

float worldBrightness(uvec2 packedLight)
{
    float sky = float(packedLight.x) * 0.25 - ambientDarkness;
    float block = float(packedLight.y) * 0.25;

    return rampLuminance(clamp(max(sky, block), 0.0, 15.0));
}

void main()
{
    vec4 viewPos = modelViewMatrix * vec4(inPosition, 1.0);

    gl_Position = projectionMatrix * viewPos;
    arrayLayer = inArrayLayer;

    fogDistance = length(viewPos.xyz);

    texCoord = (textureMatrix * vec4(inUV, 0.0, 1.0)).xy;

    // The world light the block sits in, applied here rather than baked into the colour by whoever
    // built the geometry -- so a pack can relight it, and so the sun can move without the geometry
    // being rebuilt for it.
    vec4 tint = vec4(inColor.rgb * worldBrightness(inWorldLight), inColor.a);
    if (lightingEnabled != 0)
    {
        // The light directions arrive already in eye space, put there by whatever model-view was in
        // force when they were set rather than the one this draw uses. That is load-bearing: the GUI
        // lighting works by rotating the model-view around the call that assigns them.
        vec3 normal = normalize(normalMatrix * inNormal);
        float diffuse0 = max(dot(normal, light0Direction), 0.0);
        float diffuse1 = max(dot(normal, light1Direction), 0.0);
        vec3 lighting = ambientLight + diffuse0 * light0Diffuse + diffuse1 * light1Diffuse;

        tint = vec4(clamp(inColor.rgb * lighting, 0.0, 1.0), inColor.a);
    }

    // Both, because which one survives is decided in the fragment stage. Interpolation qualifiers
    // are per-variable, so a single output cannot be switched between them by a uniform.
    vertexColorFlat = tint;
    vertexColorSmooth = tint;
}
