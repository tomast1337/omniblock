using betareborn.Blocks;
using betareborn.Client.Rendering.Core;
using betareborn.Client.Rendering.Entitys.Models;
using betareborn.Entities;
using betareborn.Items;

namespace betareborn.Client.Rendering.Entitys
{
    public class UndeadEntityRenderer : LivingEntityRenderer
    {

        protected ModelBiped modelBipedMain;

        public UndeadEntityRenderer(ModelBiped var1, float var2) : base(var1, var2)
        {
            modelBipedMain = var1;
        }

        protected override void renderMore(EntityLiving var1, float var2)
        {
            ItemStack var3 = var1.getHeldItem();
            if (var3 != null)
            {
                GLManager.GL.PushMatrix();
                modelBipedMain.bipedRightArm.transform(1.0F / 16.0F);
                GLManager.GL.Translate(-(1.0F / 16.0F), 7.0F / 16.0F, 1.0F / 16.0F);
                float var4;
                if (var3.itemID < 256 && BlockRenderer.isSideLit(Block.BLOCKS[var3.itemID].getRenderType()))
                {
                    var4 = 0.5F;
                    GLManager.GL.Translate(0.0F, 3.0F / 16.0F, -(5.0F / 16.0F));
                    var4 *= 12.0F / 16.0F;
                    GLManager.GL.Rotate(20.0F, 1.0F, 0.0F, 0.0F);
                    GLManager.GL.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
                    GLManager.GL.Scale(var4, -var4, var4);
                }
                else if (Item.ITEMS[var3.itemID].isHandheld())
                {
                    var4 = 10.0F / 16.0F;
                    GLManager.GL.Translate(0.0F, 3.0F / 16.0F, 0.0F);
                    GLManager.GL.Scale(var4, -var4, var4);
                    GLManager.GL.Rotate(-100.0F, 1.0F, 0.0F, 0.0F);
                    GLManager.GL.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
                }
                else
                {
                    var4 = 6.0F / 16.0F;
                    GLManager.GL.Translate(0.25F, 3.0F / 16.0F, -(3.0F / 16.0F));
                    GLManager.GL.Scale(var4, var4, var4);
                    GLManager.GL.Rotate(60.0F, 0.0F, 0.0F, 1.0F);
                    GLManager.GL.Rotate(-90.0F, 1.0F, 0.0F, 0.0F);
                    GLManager.GL.Rotate(20.0F, 0.0F, 0.0F, 1.0F);
                }

                dispatcher.heldItemRenderer.renderItem(var1, var3);
                GLManager.GL.PopMatrix();
            }

        }
    }

}