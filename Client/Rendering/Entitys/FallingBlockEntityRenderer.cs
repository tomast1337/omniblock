using betareborn.Blocks;
using betareborn.Client.Rendering.Core;
using betareborn.Entities;
using betareborn.Util.Maths;
using betareborn.Worlds;
using Silk.NET.OpenGL.Legacy;

namespace betareborn.Client.Rendering.Entitys
{
    public class FallingBlockEntityRenderer : EntityRenderer
    {

        private BlockRenderer renderBlocks = new BlockRenderer();

        public FallingBlockEntityRenderer()
        {
            shadowRadius = 0.5F;
        }

        public void doRenderFallingSand(EntityFallingSand var1, double var2, double var4, double var6, float var8, float var9)
        {
            GLManager.GL.PushMatrix();
            GLManager.GL.Translate((float)var2, (float)var4, (float)var6);
            loadTexture("/terrain.png");
            Block var10 = Block.BLOCKS[var1.blockID];
            World var11 = var1.getWorld();
            GLManager.GL.Disable(GLEnum.Lighting);
            renderBlocks.renderBlockFallingSand(var10, var11, MathHelper.floor_double(var1.x), MathHelper.floor_double(var1.y), MathHelper.floor_double(var1.z));
            GLManager.GL.Enable(GLEnum.Lighting);
            GLManager.GL.PopMatrix();
        }

        public override void render(Entity var1, double var2, double var4, double var6, float var8, float var9)
        {
            doRenderFallingSand((EntityFallingSand)var1, var2, var4, var6, var8, var9);
        }
    }

}