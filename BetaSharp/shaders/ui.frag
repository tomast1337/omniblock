#version 330 core
in vec2 v_TexCoord;
in vec4 v_Color;
uniform sampler2D u_Texture;
uniform int u_UseTexture;
uniform int u_TextureId;
out vec4 FragColor;

const int darkMode = 0; // [0 1]

const int TEXTURE_ID_INVENTORIES = 100;
const int TEXTURE_ID_BUTTONS_SLIDERS = 3;

void main() {
    if (u_UseTexture != 0)
        FragColor = v_Color * texture(u_Texture, v_TexCoord);
    else
        FragColor = v_Color;

   if (darkMode == 1)
   {
       if (u_TextureId == TEXTURE_ID_INVENTORIES)
           FragColor.rgb = max(FragColor.rgb / 2.0 - 0.1, 0.0);
       else if (u_TextureId == TEXTURE_ID_BUTTONS_SLIDERS)
           FragColor.rgb = max(FragColor.rgb / 1.5 - 0.1, 0.0);
   }
}
