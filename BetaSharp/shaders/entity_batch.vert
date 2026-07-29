#version 410

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec2 inUV;
layout (location = 2) in vec4 inColor;
layout (location = 3) in uint inPartId;

out vec4 vertexColor;
out vec2 texCoord;
out float fogDistance;
// Symbolic model part (see shaders/entity_parts.properties); 0 when unmapped.
flat out uint partId;

uniform mat4 projectionMatrix;

void main()
{
    gl_Position = projectionMatrix * vec4(inPosition, 1.0);

    vertexColor = inColor;
    texCoord = inUV;
    partId = inPartId;

    fogDistance = length(inPosition);
}
