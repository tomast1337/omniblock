#version 430

// Same as shaders/entity_batch.frag.

in vec4 vertexColor;
in vec2 texCoord;
in float fogDistance;
flat in uint partId;

out vec4 FragColor;

uniform sampler2D textureSampler;
uniform bool useTexture;
uniform float alphaThreshold;
uniform int entityId;

uniform vec4 fogColor;
uniform float fogDensity;
uniform float fogStart;
uniform float fogEnd;
uniform int fogMode;
uniform bool fogEnabled;

vec4 applyEntityEffects(vec4 color, int entity, uint part)
{
    return color;
}

void main()
{
    vec4 texColor = useTexture ? texture(textureSampler, texCoord) : vec4(1.0);
    vec4 finalColor = applyEntityEffects(texColor * vertexColor, entityId, partId);

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
