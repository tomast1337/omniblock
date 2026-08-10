#version 330 core

// The interface. Reads the Tessellator vertex layout like every other slot's program, because the
// UI batch goes through the same draw-command seam the rest of the client does — it is quads with
// a colour and a texture, and nothing about it needs a vertex format of its own.

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec4 inColor;
layout (location = 2) in vec2 inUV;

uniform mat4 modelViewMatrix;
uniform mat4 projectionMatrix;

out vec2 v_TexCoord;
out vec4 v_Color;

void main() {
    gl_Position = projectionMatrix * modelViewMatrix * vec4(inPosition, 1.0);
    v_TexCoord = inUV;
    v_Color = inColor;
}
