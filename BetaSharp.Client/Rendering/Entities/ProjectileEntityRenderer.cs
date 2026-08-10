using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;

namespace OmniBlock.Client.Rendering.Entities;

public class ProjectileEntityRenderer : EntityRenderer
{

    private readonly int itemIconIndex;
    private readonly float scale;

    public ProjectileEntityRenderer(int itemIconIndex, float scale = 0.5F)
    {
        this.itemIconIndex = itemIconIndex;
        this.scale = scale;
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate((float)x, (float)y, (float)z);
        GLManager.ModelView.Scale(scale, scale, scale);
        loadTexture("/gui/items.png");
        Tessellator tessellator = Tessellator.instance;
        float minU = (itemIconIndex % 16 * 16 + 0) / 256.0F;
        float maxU = (itemIconIndex % 16 * 16 + 16) / 256.0F;
        float minV = (itemIconIndex / 16 * 16 + 0) / 256.0F;
        float maxV = (itemIconIndex / 16 * 16 + 16) / 256.0F;
        float quadWidth = 1.0F;
        float xOffset = 0.5F;
        float yOffset = 0.25F;
        GLManager.ModelView.Rotate(180.0F - Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
        GLManager.ModelView.Rotate(-Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
        tessellator.startDrawingQuads();
        tessellator.setNormal(0.0F, 1.0F, 0.0F);
        tessellator.addVertexWithUV((double)(0.0F - xOffset), (double)(0.0F - yOffset), 0.0D, (double)minU, (double)maxV);
        tessellator.addVertexWithUV((double)(quadWidth - xOffset), (double)(0.0F - yOffset), 0.0D, (double)maxU, (double)maxV);
        tessellator.addVertexWithUV((double)(quadWidth - xOffset), (double)(1.0F - yOffset), 0.0D, (double)maxU, (double)minV);
        tessellator.addVertexWithUV((double)(0.0F - xOffset), (double)(1.0F - yOffset), 0.0D, (double)minU, (double)minV);
        tessellator.draw(ProgramSlot.Entities);
        GLManager.ModelView.Pop();
    }
}
