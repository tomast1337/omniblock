#version 330 core

// The root of the fallback chain: untextured, unlit geometry, tinted per vertex or as a whole.
// Attribute 1 is read whether or not the Tessellator bound an array to it — geometry drawn with one
// colour throughout supplies no array and the tint arrives as the attribute's default value.

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec4 inColor;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;

flat out vec4 vertexColorFlat;
out vec4 vertexColorSmooth;
out float fogDistance;

void main()
{
    vec4 viewPos = modelViewMatrix * vec4(inPosition, 1.0);

    gl_Position = projectionMatrix * viewPos;
    fogDistance = length(viewPos.xyz);

    // Both, because which one survives is decided in the fragment stage. Interpolation qualifiers
    // are per-variable, so a single output cannot be switched between them by a uniform.
    vertexColorFlat = inColor;
    vertexColorSmooth = inColor;
}
