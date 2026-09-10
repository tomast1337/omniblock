using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

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
        var fuse = target.Behaviors.Find<PrimedExplosiveBehavior>()?.FuseTicks(target) ?? 0;

        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate((float)x, (float)y, (float)z);
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
            var scale = 1.0F + flashProgress * 0.3F;
            RenderSystem.ModelView.Scale(scale, scale, scale);
        }

        flashProgress = (1.0F - (fuse - tickDelta + 1.0F) / 100.0F) * 0.8F;
        loadTexture("/terrain.png");
        BlockRenderer.RenderBlockOnInventory(target.World.Content.Blocks, _block, 0, target.GetBrightnessAtEyes(tickDelta), Tessellator.instance);
        if (fuse / 5 % 2 == 0)
        {
            // Texturing and lighting are shader uniforms rather than pipeline state, so they stay
            // as they are. The flash weights itself against what is already there rather than
            // against its own alpha, which is the one place that blend mode is used.
            RenderSystem.TextureEnabled = false;
            RenderSystem.LightingEnabled = false;
            RenderSystem.State.Apply(RenderState.Entity with
            {
                Blend = BlendMode.SourceToDestinationAlpha
            });
            RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, flashProgress);
            BlockRenderer.RenderBlockOnInventory(target.World.Content.Blocks, _block, 0, 1.0F, Tessellator.instance);
            RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
            RenderSystem.State.Apply(RenderState.Entity);
            RenderSystem.LightingEnabled = true;
            RenderSystem.TextureEnabled = true;
        }

        RenderSystem.ModelView.Pop();
    }
}
