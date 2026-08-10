#version 330 core

in vec2 v_TexCoord;
in vec4 v_Color;

uniform sampler2D u_Texture;

// Which interface texture is being sampled, named by shaders/ui_textures.properties. A pack tells
// widgets apart by it — nothing here can, since they all arrive as quads out of one batch.
uniform int u_TextureId;

out vec4 FragColor;

const int darkMode = 0; // [0 1]

const int TEXTURE_ID_INVENTORIES = 100;
const int TEXTURE_ID_BUTTONS_SLIDERS = 3;

void main() {
    FragColor = v_Color * texture(u_Texture, v_TexCoord);

   if (darkMode == 1)
   {
       if (u_TextureId == TEXTURE_ID_INVENTORIES)
           FragColor.rgb = max(FragColor.rgb / 2.0 - 0.1, 0.0);
       else if (u_TextureId == TEXTURE_ID_BUTTONS_SLIDERS)
           FragColor.rgb = max(FragColor.rgb / 1.5 - 0.1, 0.0);
   }
}
