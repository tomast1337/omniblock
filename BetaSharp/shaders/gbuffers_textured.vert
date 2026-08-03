#version 330 core

// Textured and tinted, unlit. Attribute 1 is read whether or not the Tessellator bound an array to
// it — geometry drawn with one colour throughout supplies no array and the tint arrives as the
// attribute's default value.

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec4 inColor;
layout (location = 2) in vec2 inUV;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;
uniform mat4 textureMatrix;

flat out vec4 vertexColorFlat;
out vec4 vertexColorSmooth;
out vec2 texCoord;
out float fogDistance;

void main()
{
    vec4 viewPos = modelViewMatrix * vec4(inPosition, 1.0);

    gl_Position = projectionMatrix * viewPos;
    fogDistance = length(viewPos.xyz);

    // Through the texture matrix, which the animated textures scroll and the compass and clock
    // rotate. Identity for everything else, but not something a draw can opt out of.
    texCoord = (textureMatrix * vec4(inUV, 0.0, 1.0)).xy;

    // Both, because which one survives is decided in the fragment stage. Interpolation qualifiers
    // are per-variable, so a single output cannot be switched between them by a uniform.
    vertexColorFlat = inColor;
    vertexColorSmooth = inColor;
}
