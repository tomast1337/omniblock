using betareborn.Client.Rendering.Core;
using betareborn.Client.Rendering.Entitys.Models;
using betareborn.Entities;

namespace betareborn.Client.Rendering.Entitys
{
    public class SquidEntityRenderer : LivingEntityRenderer
    {

        public SquidEntityRenderer(ModelBase var1, float var2) : base(var1, var2)
        {
        }

        public void func_21008_a(EntitySquid var1, double var2, double var4, double var6, float var8, float var9)
        {
            base.doRenderLiving(var1, var2, var4, var6, var8, var9);
        }

        protected void func_21007_a(EntitySquid var1, float var2, float var3, float var4)
        {
            float var5 = var1.field_21088_b + (var1.field_21089_a - var1.field_21088_b) * var4;
            float var6 = var1.field_21086_f + (var1.field_21087_c - var1.field_21086_f) * var4;
            GLManager.GL.Translate(0.0F, 0.5F, 0.0F);
            GLManager.GL.Rotate(180.0F - var3, 0.0F, 1.0F, 0.0F);
            GLManager.GL.Rotate(var5, 1.0F, 0.0F, 0.0F);
            GLManager.GL.Rotate(var6, 0.0F, 1.0F, 0.0F);
            GLManager.GL.Translate(0.0F, -1.2F, 0.0F);
        }

        protected void func_21005_a(EntitySquid var1, float var2)
        {
        }

        protected float func_21006_b(EntitySquid var1, float var2)
        {
            float var3 = var1.field_21082_j + (var1.field_21083_i - var1.field_21082_j) * var2;
            return var3;
        }

        protected override void preRenderCallback(EntityLiving var1, float var2)
        {
            func_21005_a((EntitySquid)var1, var2);
        }

        protected override float func_170_d(EntityLiving var1, float var2)
        {
            return func_21006_b((EntitySquid)var1, var2);
        }

        protected override void rotateCorpse(EntityLiving var1, float var2, float var3, float var4)
        {
            func_21007_a((EntitySquid)var1, var2, var3, var4);
        }

        public override void doRenderLiving(EntityLiving var1, double var2, double var4, double var6, float var8, float var9)
        {
            func_21008_a((EntitySquid)var1, var2, var4, var6, var8, var9);
        }

        public override void render(Entity var1, double var2, double var4, double var6, float var8, float var9)
        {
            func_21008_a((EntitySquid)var1, var2, var4, var6, var8, var9);
        }
    }

}