using betareborn.Client.Rendering.Core;
using Silk.NET.OpenGL.Legacy;

namespace betareborn.Client.Textures
{
    public class TextureFX : java.lang.Object
    {
        public byte[] imageData = new byte[1024];
        public int iconIndex;
        public bool anaglyphEnabled = false;
        public int textureId = 0;
        public int tileSize = 1;
        public FXImage tileImage = FXImage.Terrain;

        public enum FXImage
        {
            Terrain,
            Items
        }

        public TextureFX(int iconIdx)
        {
            iconIndex = iconIdx;
        }

        public virtual void onTick()
        {
        }

        public void bindImage(TextureManager var1)
        {
            if (tileImage == FXImage.Terrain)
            {
                GLManager.GL.BindTexture(GLEnum.Texture2D, (uint)var1.getTexture("/terrain.png"));
            }
            else if (tileImage == FXImage.Items)
            {
                GLManager.GL.BindTexture(GLEnum.Texture2D, (uint)var1.getTexture("/gui/items.png"));

            }

        }
    }
}