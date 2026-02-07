using betareborn.Blocks;
using betareborn.Rendering;
using betareborn.Worlds;
using Silk.NET.OpenGL.Legacy;

namespace betareborn.TileEntities
{
    public class TileEntityRendererPiston : TileEntitySpecialRenderer
    {

        private RenderBlocks field_31071_b;

        public void func_31070_a(TileEntityPiston var1, double var2, double var4, double var6, float var8)
        {
            Block var9 = Block.blocksList[var1.getPushedBlockId()];
            if (var9 != null && var1.getProgress(var8) < 1.0F)
            {
                Tessellator var10 = Tessellator.instance;
                bindTextureByName("/terrain.png");
                RenderHelper.disableStandardItemLighting();
                GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
                GLManager.GL.Enable(GLEnum.Blend);
                GLManager.GL.Disable(GLEnum.CullFace);
                if (Minecraft.isAmbientOcclusionEnabled())
                {
                    GLManager.GL.ShadeModel(GLEnum.Smooth);
                }
                else
                {
                    GLManager.GL.ShadeModel(GLEnum.Flat);
                }

                var10.startDrawingQuads();
                var10.setTranslationD((double)((float)var2 - (float)var1.x + var1.getRenderOffsetX(var8)), (double)((float)var4 - (float)var1.y + var1.getRenderOffsetY(var8)), (double)((float)var6 - (float)var1.z + var1.getRenderOffsetZ(var8)));
                var10.setColorOpaque(1, 1, 1);
                if (var9 == Block.pistonExtension && var1.getProgress(var8) < 0.5F)
                {
                    field_31071_b.func_31079_a(var9, var1.x, var1.y, var1.z, false);
                }
                else if (var1.isSource() && !var1.isExtending())
                {
                    Block.pistonExtension.func_31052_a_(((BlockPistonBase)var9).func_31040_i());
                    field_31071_b.func_31079_a(Block.pistonExtension, var1.x, var1.y, var1.z, var1.getProgress(var8) < 0.5F);
                    Block.pistonExtension.func_31051_a();
                    var10.setTranslationD((double)((float)var2 - (float)var1.x), (double)((float)var4 - (float)var1.y), (double)((float)var6 - (float)var1.z));
                    field_31071_b.func_31078_d(var9, var1.x, var1.y, var1.z);
                }
                else
                {
                    field_31071_b.func_31075_a(var9, var1.x, var1.y, var1.z);
                }

                var10.setTranslationD(0.0D, 0.0D, 0.0D);
                var10.draw();
                RenderHelper.enableStandardItemLighting();
            }

        }

        public override void func_31069_a(World var1)
        {
            field_31071_b = new RenderBlocks(var1, Tessellator.instance);
        }

        public override void renderTileEntityAt(TileEntity var1, double var2, double var4, double var6, float var8)
        {
            func_31070_a((TileEntityPiston)var1, var2, var4, var6, var8);
        }
    }

}