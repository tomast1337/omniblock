#version 330 core

flat in vec4 vertexColorFlat;
in vec4 vertexColorSmooth;
in vec2 texCoord;
in float fogDistance;
flat in int arrayLayer;

uniform sampler2D textureSampler;

// The named terrain tiles. A draw naming a layer means its coordinates are inside that one tile,
// which is what lets a pack ship a texture at its own resolution.
uniform sampler2DArray arraySampler;
uniform float alphaThreshold;
uniform int shadeModel;

uniform int fogEnabled;
uniform int fogMode;
uniform vec4 fogColor;
uniform float fogStart;
uniform float fogEnd;
uniform float fogDensity;

out vec4 FragColor;

void main()
{
    vec4 tint = shadeModel == 1 ? vertexColorSmooth : vertexColorFlat;
    vec4 sampled = arrayLayer < 0
        ? texture(textureSampler, texCoord)
        : texture(arraySampler, vec3(texCoord, float(arrayLayer)));

    vec4 color = tint * sampled;

    // Negative when the alpha test is off, so there is one uniform rather than a float and a flag.
    // Tested after the tint is applied, so a draw fading something out can drop it entirely.
    if (color.a < alphaThreshold)
    {
        discard;
    }

    if (fogEnabled != 0)
    {
        float fogFactor = fogMode == 0
            ? clamp((fogEnd - fogDistance) / (fogEnd - fogStart), 0.0, 1.0)
            : clamp(exp(-fogDensity * fogDistance), 0.0, 1.0);

        color = mix(fogColor, color, fogFactor);
    }

    FragColor = color;
}
