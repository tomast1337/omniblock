using betareborn.Rendering;
using betareborn.Worlds;

namespace betareborn.TileEntities
{
    public abstract class TileEntitySpecialRenderer
    {
        protected TileEntityRenderer tileEntityRenderer;

        public abstract void renderTileEntityAt(BlockEntity var1, double var2, double var4, double var6, float var8);

        protected void bindTextureByName(String var1)
        {
            RenderEngine var2 = tileEntityRenderer.renderEngine;
            var2.bindTexture(var2.getTexture(var1));
        }

        public void setTileEntityRenderer(TileEntityRenderer var1)
        {
            tileEntityRenderer = var1;
        }

        public virtual void func_31069_a(World var1)
        {
        }

        public FontRenderer getFontRenderer()
        {
            return tileEntityRenderer.getFontRenderer();
        }
    }

}