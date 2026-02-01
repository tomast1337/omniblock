#version 460

layout (location = 0) in vec3 position;
layout (location = 1) in vec2 uv;
layout (location = 2) in vec4 color;

out vec4 vertexColor;
out vec2 texCoord;
out float fogDistance;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;

void main() 
{
    vec4 viewPos = modelViewMatrix * vec4(position, 1.0);
    gl_Position = projectionMatrix * viewPos;
    
    vertexColor = color;
    texCoord = uv;

    fogDistance = length(viewPos.xyz);
}