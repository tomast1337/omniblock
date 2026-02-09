using betareborn.Client.Rendering.Core;
using betareborn.Client.Rendering.Entitys.Models;
using betareborn.Entities;

namespace betareborn.Client.Rendering.Entitys
{
    public class GiantEntityRenderer : LivingEntityRenderer
    {

        private float scale;

        public GiantEntityRenderer(ModelBase var1, float var2, float var3) : base(var1, var2 * var3)
        {
            scale = var3;
        }

        protected void preRenderScale(EntityGiantZombie var1, float var2)
        {
            GLManager.GL.Scale(scale, scale, scale);
        }

        protected override void preRenderCallback(EntityLiving var1, float var2)
        {
            preRenderScale((EntityGiantZombie)var1, var2);
        }
    }

}