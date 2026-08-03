#version 330 core

flat in vec4 vertexColorFlat;
in vec4 vertexColorSmooth;
in float fogDistance;

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
    vec4 color = shadeModel == 1 ? vertexColorSmooth : vertexColorFlat;

    // Negative when the alpha test is off, so there is one uniform rather than a float and a flag.
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
