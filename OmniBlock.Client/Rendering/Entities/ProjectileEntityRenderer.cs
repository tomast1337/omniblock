using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;

namespace OmniBlock.Client.Rendering.Entities;

public class ProjectileEntityRenderer : EntityRenderer
{
    private readonly ResourceLocation _item;
    private readonly float scale;

    public ProjectileEntityRenderer(ResourceLocation item, float scale = 0.5F)
    {
        _item = item;
        this.scale = scale;
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        var itemIconIndex = target.World.Content.Items.Get(_item).GetTextureId(0);
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate((float)x, (float)y, (float)z);
        GLManager.ModelView.Scale(scale, scale, scale);
        loadTexture("/gui/items.png");
        var tessellator = Tessellator.instance;
        var minU = (itemIconIndex % 16 * 16 + 0) / 256.0F;
        var maxU = (itemIconIndex % 16 * 16 + 16) / 256.0F;
        var minV = (itemIconIndex / 16 * 16 + 0) / 256.0F;
        var maxV = (itemIconIndex / 16 * 16 + 16) / 256.0F;
        var quadWidth = 1.0F;
        var xOffset = 0.5F;
        var yOffset = 0.25F;
        GLManager.ModelView.Rotate(180.0F - Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
        GLManager.ModelView.Rotate(-Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
        tessellator.startDrawingQuads();
        tessellator.setNormal(0.0F, 1.0F, 0.0F);
        tessellator.addVertexWithUV(0.0F - xOffset, 0.0F - yOffset, 0.0D, minU, maxV);
        tessellator.addVertexWithUV(quadWidth - xOffset, 0.0F - yOffset, 0.0D, maxU, maxV);
        tessellator.addVertexWithUV(quadWidth - xOffset, 1.0F - yOffset, 0.0D, maxU, minV);
        tessellator.addVertexWithUV(0.0F - xOffset, 1.0F - yOffset, 0.0D, minU, minV);
        tessellator.draw(ProgramSlot.Entities);
        GLManager.ModelView.Pop();
    }
}
