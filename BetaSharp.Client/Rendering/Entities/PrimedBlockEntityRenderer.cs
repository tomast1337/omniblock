using BetaSharp.Blocks;
using BetaSharp.Client.Rendering.Blocks;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Draws a primed explosive as its block, swelling and flashing white as the fuse runs out. The
///     fuse is read through <see cref="PrimedExplosiveBehavior" /> rather than an entity class, and
///     which block to draw is declared in the definition's renderer entry.
/// </summary>
public class PrimedBlockEntityRenderer : EntityRenderer
{
    private readonly Block _block;

    public PrimedBlockEntityRenderer(Block block, float shadowRadius)
    {
        _block = block;
        ShadowRadius = shadowRadius;
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        int fuse = target.Behaviors.Find<PrimedExplosiveBehavior>()?.FuseTicks(target) ?? 0;

        GLManager.GL.PushMatrix();
        GLManager.GL.Translate((float)x, (float)y, (float)z);
        float flashProgress;
        if (fuse - tickDelta + 1.0F < 10.0F)
        {
            flashProgress = 1.0F - (fuse - tickDelta + 1.0F) / 10.0F;
            if (flashProgress < 0.0F)
            {
                flashProgress = 0.0F;
            }

            if (flashProgress > 1.0F)
            {
                flashProgress = 1.0F;
            }

            flashProgress *= flashProgress;
            flashProgress *= flashProgress;
            float scale = 1.0F + flashProgress * 0.3F;
            GLManager.GL.Scale(scale, scale, scale);
        }

        flashProgress = (1.0F - (fuse - tickDelta + 1.0F) / 100.0F) * 0.8F;
        loadTexture("/terrain.png");
        BlockRenderer.RenderBlockOnInventory(_block, 0, target.GetBrightnessAtEyes(tickDelta), Tessellator.instance);
        if (fuse / 5 % 2 == 0)
        {
            GLManager.GL.Disable(GLEnum.Texture2D);
            GLManager.GL.Disable(GLEnum.Lighting);
            GLManager.GL.Enable(GLEnum.Blend);
            GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.DstAlpha);
            GLManager.GL.Color4(1.0F, 1.0F, 1.0F, flashProgress);
            BlockRenderer.RenderBlockOnInventory(_block, 0, 1.0F, Tessellator.instance);
            GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
            GLManager.GL.Disable(GLEnum.Blend);
            GLManager.GL.Enable(GLEnum.Lighting);
            GLManager.GL.Enable(GLEnum.Texture2D);
        }
        GLManager.GL.PopMatrix();
    }
}
