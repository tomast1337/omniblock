#version 330 core
in vec2 v_TexCoord;
in vec4 v_Color;
uniform sampler2D u_Texture;
uniform int u_UseTexture;
uniform int u_TextureId;
out vec4 FragColor;
void main() {
    if (u_UseTexture != 0)
        FragColor = v_Color * texture(u_Texture, v_TexCoord);
    else
        FragColor = v_Color;
}
