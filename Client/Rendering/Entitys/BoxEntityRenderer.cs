using betareborn.Client.Rendering.Core;
using betareborn.Entities;

namespace betareborn.Client.Rendering.Entitys
{
    public class BoxEntityRenderer : EntityRenderer
    {

        public override void render(Entity var1, double var2, double var4, double var6, float var8, float var9)
        {
            GLManager.GL.PushMatrix();
            renderShape(var1.boundingBox, var2 - var1.lastTickPosX, var4 - var1.lastTickPosY, var6 - var1.lastTickPosZ);
            GLManager.GL.PopMatrix();
        }
    }

}