using betareborn.Client.Rendering.Core;
using betareborn.Client.Rendering.Entitys.Models;
using betareborn.Entities;

namespace betareborn.Client.Rendering.Entitys
{
    public class GhastEntityRenderer : LivingEntityRenderer
    {

        public GhastEntityRenderer() : base(new ModelGhast(), 0.5F)
        {
        }

        protected void render(EntityGhast var1, float var2)
        {
            float var4 = (var1.prevAttackCounter + (var1.attackCounter - var1.prevAttackCounter) * var2) / 20.0F;
            if (var4 < 0.0F)
            {
                var4 = 0.0F;
            }

            var4 = 1.0F / (var4 * var4 * var4 * var4 * var4 * 2.0F + 1.0F);
            float var5 = (8.0F + var4) / 2.0F;
            float var6 = (8.0F + 1.0F / var4) / 2.0F;
            GLManager.GL.Scale(var6, var5, var6);
            GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
        }

        protected override void preRenderCallback(EntityLiving var1, float var2)
        {
            render((EntityGhast)var1, var2);
        }
    }

}