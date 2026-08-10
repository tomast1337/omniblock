#version 410

in vec4 vertexColor;
in vec2 texCoord;
in float fogDistance;
// Which part of the model this fragment belongs to — see shaders/entity_parts.properties.
flat in uint partId;

out vec4 FragColor;

uniform sampler2D textureSampler;
uniform bool useTexture;
uniform float alphaThreshold;
// Which entity is being drawn — see shaders/entity_textures.properties. 0 means unmapped.
uniform int entityId;

uniform vec4 fogColor;
uniform float fogDensity;
uniform float fogStart;
uniform float fogEnd;
uniform int fogMode;
uniform bool fogEnabled;

// The extension point the two id maps exist to serve: everything drawn arrives here tagged with
// which entity it belongs to and which part of that entity. The default is a passthrough, so
// leaving this alone renders exactly as it did before the ids were introduced.
//
//   if (entity == 4 && part == 11) { return color * vec3(0.0, 1.0, 0.0); }  // creeper body
//
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
