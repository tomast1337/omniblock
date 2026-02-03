#version 460

layout(location = 0) in vec3 inPosition;
layout(location = 1) in uvec2 inUV;
layout(location = 2) in vec4 inColor;
layout(location = 3) in uint inLight;

out vec4 vertexColor;
out vec2 texCoord;
out float fogDistance;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;

const float POSITION_SCALE_INV = 32.0 / 32767.0;

vec3 unpackPosition(vec3 packedPos)
{
    return packedPos * POSITION_SCALE_INV;
}

float unpackSkyLight(uint light)
{
    return float((light >> 4) & 0xFu) / 15.0;
}

float unpackBlockLight(uint light)
{
    return float(light & 0xFu) / 15.0;
}

vec3 snap(vec3 pos) 
{
    float step = 1.0 / 64.0;
    return round(pos / step) * step;
}

void main() 
{
    vec3 position = unpackPosition(inPosition);
    //position = snap(position);
    
    vec2 uv = vec2(inUV & 0x7FFFu) / 32767.0;
    
    uvec2 signBits = (inUV >> 15u) & 1u;
    
    const float epsilon = 1.0 / 65536.0;
    vec2 bias = vec2(
        (signBits.x == 0u) ? epsilon : -epsilon,
        (signBits.y == 0u) ? epsilon : -epsilon
    );
    
    uv += bias;
    
    vec4 color = inColor;

    vec4 viewPos = modelViewMatrix * vec4(position, 1.0);
    gl_Position = projectionMatrix * viewPos;
    
    vertexColor = color;
    texCoord = uv;

    fogDistance = length(viewPos.xyz);
}