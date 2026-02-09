using betareborn.Client.Rendering.Core;
using betareborn.Client.Rendering.Entitys.Models;
using betareborn.Entities;
using Silk.NET.OpenGL.Legacy;

namespace betareborn.Client.Rendering.Entitys
{
    public class SpiderEntityRenderer : LivingEntityRenderer
    {

        public SpiderEntityRenderer() : base(new ModelSpider(), 1.0F)
        {
            setRenderPassModel(new ModelSpider());
        }

        protected float setSpiderDeathMaxRotation(EntitySpider var1)
        {
            return 180.0F;
        }

        protected bool setSpiderEyeBrightness(EntitySpider var1, int var2, float var3)
        {
            if (var2 != 0)
            {
                return false;
            }
            else if (var2 != 0)
            {
                return false;
            }
            else
            {
                loadTexture("/mob/spider_eyes.png");
                float var4 = (1.0F - var1.getEntityBrightness(1.0F)) * 0.5F;
                GLManager.GL.Enable(GLEnum.Blend);
                GLManager.GL.Disable(GLEnum.AlphaTest);
                GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
                GLManager.GL.Color4(1.0F, 1.0F, 1.0F, var4);
                return true;
            }
        }

        protected override float getDeathMaxRotation(EntityLiving var1)
        {
            return setSpiderDeathMaxRotation((EntitySpider)var1);
        }

        protected override bool shouldRenderPass(EntityLiving var1, int var2, float var3)
        {
            return setSpiderEyeBrightness((EntitySpider)var1, var2, var3);
        }
    }

}