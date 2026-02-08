using betareborn.Client.Models;
using betareborn.Entities;

namespace betareborn.Client.Rendering
{
    public class RenderCow : RenderLiving
    {

        public RenderCow(ModelBase var1, float var2) : base(var1, var2)
        {
        }

        public void renderCow(EntityCow var1, double var2, double var4, double var6, float var8, float var9)
        {
            base.doRenderLiving(var1, var2, var4, var6, var8, var9);
        }

        public override void doRenderLiving(EntityLiving var1, double var2, double var4, double var6, float var8, float var9)
        {
            renderCow((EntityCow)var1, var2, var4, var6, var8, var9);
        }

        public override void doRender(Entity var1, double var2, double var4, double var6, float var8, float var9)
        {
            renderCow((EntityCow)var1, var2, var4, var6, var8, var9);
        }
    }

}