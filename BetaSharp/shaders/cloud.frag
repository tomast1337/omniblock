#version 410

in vec4 v_Color;
in vec2 v_TexCoord;
in vec3 v_LocalPos;
out vec4 FragColor;

uniform sampler2D u_Texture;
uniform vec3 fog;
uniform vec3 u_CloudOffset;
uniform float u_CloudScale;

// [preset:2d] minParallax=0 maxParallax=0 cloudHeight=4.0 minGradient=0 shadow=0.66 maxGradient=0 smoothWidth=0.5 smoothSamples=4 closeFade=0.0 smoothTop=0 smoothEdge=0 smoothBottom=0 smoothBottomTopDown=0
// [preset:medium:2d] minParallax=4 maxParallax=32 maxGradient=10 closeFade=8.0 smoothEdge=1
// [preset:high:medium] maxParallax=42 minGradient=1 maxGradient=16 smoothTop=1 smoothBottom=1 smoothBottomTopDown=1

const int minParallax = 4; // [0 - 8]
const int maxParallax = 32; // [0 - 100]
const int texSize = 256;
const float cloudHeight = 4.0; // [1.0 - 8.0]
const float scaledTextSize = cloudHeight / texSize;
const float shadow = 0.66; // [0.00 - 1.00]

const int minGradient = 0; // [0 - 6]
const int maxGradient = 10; // [0 - 32]

const float smoothWidth = 0.5; // [ 0.1 - 10.0]
const float gradientWidth = smoothWidth / texSize;

const int smoothSamples = 4; // [1 - 10]
const float gradiantStep = gradientWidth / smoothSamples;

const float closeFade = 8.0; // [0.0 - 10.0]

const int smoothTop = 0; // [0 1]
const int smoothEdge = 1; // [0 1]
const int smoothBottom = 0; // [0 1]
const int smoothBottomTopDown = 0; // [0 1]

void main()
{
    vec3 normal = v_LocalPos + u_CloudOffset;

    float dist = length(normal.xz) * u_CloudScale;
    float fogFactor = clamp((fog.y - dist) / (fog.y - fog.x), 0.0, 1.0);

    vec4 color = v_Color;
    float len;
    if (closeFade > 0) {
        len = length(normal);
        color.a *= clamp(len / closeFade - closeFade / 8.0, 0.0, 1.0);
    }

    if (color.a * fogFactor < 0.001) discard;
    vec4 texColor;

    if (maxParallax <= 0 && minParallax <= 0)
    {
        texColor = texture(u_Texture, v_TexCoord);
        if (texColor.a < 0.1) discard;
    }
    else
    {
        if (closeFade <= 0) {
            len = length(normal);
        }

        texColor = texture(u_Texture, v_TexCoord);
        if (len < 1.0) len = 1.0;

        int paralaxCount;
        if (maxParallax <= minParallax) paralaxCount = minParallax;
        else paralaxCount = int((fogFactor * fogFactor) * (maxParallax - minParallax)) + minParallax;
        float paralaxStep = len / (scaledTextSize / paralaxCount);

        if (texColor.a < 0.1) {

            // flip expand direction when seen from above
            if (normal.y < 0) normal = -normal;
            vec2 texOff = normal.xz / paralaxStep;

            if (smoothEdge <= 0) {
                for (int i = paralaxCount; i > 0; i--) {
                    texColor = texture(u_Texture, v_TexCoord + texOff * i);
                    if (texColor.a >= 0.1) break;
                }
            } else if (smoothTop <= 0) {
                texColor = texture(u_Texture, v_TexCoord + texOff * paralaxCount);
                if (texColor.a < 0.1) {
                    float dens = 0;
                    vec4 c = texColor;
                    for (int i = paralaxCount - 1; i > 0; i--) {
                        c = texture(u_Texture, v_TexCoord + texOff * i);
                        if (c.a >= 0.1) {
                            texColor = c;
                            if (++dens >= smoothSamples) break;
                        }
                    }

                    texColor.a *= dens / smoothSamples;
                }
            } else {
                float dens = 0;
                vec4 c = texColor;
                for (int i = paralaxCount; i > 0; i--) {
                    c = texture(u_Texture, v_TexCoord + texOff * i);
                    if (c.a >= 0.1) {
                        texColor = c;
                        if (++dens >= smoothSamples) break;
                    }
                }

                texColor.a *= dens / smoothSamples;
            }

            if (texColor.a < 0.1) discard;

        }
        else if (normal.y >= 0)
        {

            // smooth gradient from highlight (sides) to shadow (underside)
            float gradiant = 0;

            int gradiantCount;
            if (maxGradient <= minGradient) gradiantCount = minGradient;
            else gradiantCount = int((fogFactor * fogFactor) * (maxGradient - minGradient)) + minGradient;

            if (gradiantCount > 0) {
                float stepSize = gradientWidth / gradiantCount;
                vec2 texOff = normal.xz / (len / (gradientWidth / gradiantCount));

                for (int i = gradiantCount; i > 0; i--) {
                    if (texture(u_Texture, v_TexCoord - (texOff * i)).a < 0.1) gradiant += 1;
                }

                gradiant /= float(gradiantCount);

                texColor.rgb = texColor.rgb * (gradiant + shadow * (1 - gradiant));

                if (smoothBottom > 0) {
                    float dens = 0;
                    texOff = normal.xz / (len / (scaledTextSize / paralaxStep));

                    for (int i = smoothSamples; i > 0; i--) {
                        if (texture(u_Texture, v_TexCoord + normal.xz / (paralaxStep * i)).a >= 0.1) {
                            if (++dens >= smoothSamples) break;
                        }
                    }

                    texColor.a *= dens / smoothSamples;
                }
            }

        }
        else if (smoothBottomTopDown > 0)
        {
            float dens = 0;
            vec2 texOff = normal.xz / paralaxStep;

            for (int i = smoothSamples; i > 0; i--) {
                if (texture(u_Texture, v_TexCoord - texOff * i).a >= 0.1) {
                    if (++dens >= smoothSamples) break;
                }
            }

            texColor.a *= dens / smoothSamples;
        }
    }

    FragColor = texColor * color;
    FragColor.a *= fogFactor;
}
