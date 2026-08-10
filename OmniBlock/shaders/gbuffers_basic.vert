#version 330 core

// The root of the fallback chain: untextured geometry, tinted per vertex or as a whole. Attributes 1
// and 3 are read whether or not the Tessellator bound arrays to them — geometry with one colour or
// one facing throughout supplies no array and the value arrives as the attribute's default.

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec4 inColor;
layout (location = 3) in vec3 inNormal;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;
uniform mat3 normalMatrix;

// Untextured geometry can be lit — the debug shapes are, and nothing turns lighting off around them.
// Without this there is no slot such geometry can name, and it would have to stay on a fallback that
// this chain exists to remove.
uniform int lightingEnabled;
uniform vec3 light0Direction;
uniform vec3 light0Diffuse;
uniform vec3 light1Direction;
uniform vec3 light1Diffuse;
uniform vec3 ambientLight;

flat out vec4 vertexColorFlat;
out vec4 vertexColorSmooth;
out float fogDistance;

void main()
{
    vec4 viewPos = modelViewMatrix * vec4(inPosition, 1.0);

    gl_Position = projectionMatrix * viewPos;
    fogDistance = length(viewPos.xyz);

    vec4 tint = inColor;
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
