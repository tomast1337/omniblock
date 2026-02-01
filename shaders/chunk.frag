#version 460

in vec4 vertexColor;
in vec2 texCoord;

out vec4 FragColor;

uniform sampler2D textureSampler;

void main() 
{
    vec4 texColor = texture2D(textureSampler, texCoord);
    FragColor = texColor * vertexColor;
}