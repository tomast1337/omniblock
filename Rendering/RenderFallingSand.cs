using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Util.Maths;
using betareborn.Worlds;
using Silk.NET.OpenGL.Legacy;

namespace betareborn.Rendering
{
    public class RenderFallingSand : Render
    {

        private RenderBlocks field_197_d = new RenderBlocks();

        public RenderFallingSand()
        {
            shadowSize = 0.5F;
        }

        public void doRenderFallingSand(EntityFallingSand var1, double var2, double var4, double var6, float var8, float var9)
        {
            GLManager.GL.PushMatrix();
            GLManager.GL.Translate((float)var2, (float)var4, (float)var6);
            loadTexture("/terrain.png");
            Block var10 = Block.BLOCKS[var1.blockID];
            World var11 = var1.getWorld();
            GLManager.GL.Disable(GLEnum.Lighting);
            field_197_d.renderBlockFallingSand(var10, var11, MathHelper.floor_double(var1.posX), MathHelper.floor_double(var1.posY), MathHelper.floor_double(var1.posZ));
            GLManager.GL.Enable(GLEnum.Lighting);
            GLManager.GL.PopMatrix();
        }

        public override void doRender(Entity var1, double var2, double var4, double var6, float var8, float var9)
        {
            doRenderFallingSand((EntityFallingSand)var1, var2, var4, var6, var8, var9);
        }
    }

}