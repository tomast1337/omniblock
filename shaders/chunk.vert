#version 460

in vec3 position;
in vec4 color;
in vec2 uv;

out vec4 vertexColor;
out vec2 texCoord;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;

void main() 
{
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
    vertexColor = color;
    texCoord = uv;
}