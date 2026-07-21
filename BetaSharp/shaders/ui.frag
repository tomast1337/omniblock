#version 330 core
in vec2 v_TexCoord;
in vec4 v_Color;
uniform sampler2D u_Texture;
uniform int u_UseTexture;
uniform int u_TextureId;
out vec4 FragColor;

const int darkMode = 0; // [0 1]

void main() {
    if (u_UseTexture != 0)
        FragColor = v_Color * texture(u_Texture, v_TexCoord);
    else
        FragColor = v_Color;

   if (darkMode == 1)
   {
       // inventories
       if (u_TextureId == 100)
           FragColor.rgb = FragColor.rgb / 2.0 - 0.1;
       // ui buttons and sliders
       else if (u_TextureId == 3)
           FragColor.rgb = FragColor.rgb / 1.5 - 0.1;
   }
}
