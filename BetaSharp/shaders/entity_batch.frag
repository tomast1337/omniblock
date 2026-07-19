#version 410

in vec4 vertexColor;
in vec2 texCoord;
in float fogDistance;

out vec4 FragColor;

uniform sampler2D textureSampler;
uniform bool useTexture;
uniform float alphaThreshold;

uniform vec4 fogColor;
uniform float fogDensity;
uniform float fogStart;
uniform float fogEnd;
uniform int fogMode;
uniform bool fogEnabled;

void main()
{
    vec4 texColor = useTexture ? texture(textureSampler, texCoord) : vec4(1.0);
    vec4 finalColor = texColor * vertexColor;

    if (finalColor.a < alphaThreshold)
    {
        discard;
    }

    if (fogEnabled)
    {
        float fogFactor;

        if (fogMode == 0)
        {
            fogFactor = (fogEnd - fogDistance) / (fogEnd - fogStart);
        }
        else
        {
            fogFactor = exp(-fogDensity * fogDistance);
        }

        fogFactor = clamp(fogFactor, 0.0, 1.0);

        FragColor = mix(fogColor, finalColor, fogFactor);
    }
    else
    {
        FragColor = finalColor;
    }
}
