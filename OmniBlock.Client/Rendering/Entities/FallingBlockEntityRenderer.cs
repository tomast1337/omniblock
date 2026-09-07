using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
///     Draws a falling block as the block it carries, read through
///     <see cref="SettleAsBlockBehavior" /> rather than an entity class.
/// </summary>
public class FallingBlockEntityRenderer : EntityRenderer
{
    public FallingBlockEntityRenderer(float shadowRadius) => ShadowRadius = shadowRadius;

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        var blockId = target.Behaviors.Find<SettleAsBlockBehavior>()?.BlockId(target) ?? 0;
        if (blockId == 0) return;

        GLManager.ModelView.Push();
        GLManager.ModelView.Translate((float)x, (float)y, (float)z);
        loadTexture("/terrain.png");
        var block = global::OmniBlock.Registries.ContentRuntime.Current.Blocks.GetByProtocolId(blockId);
        var world = target.World;
        GLManager.LightingEnabled = false;
        BlockRenderer.RenderBlockFallingSand(block, world, MathHelper.Floor(target.X), MathHelper.Floor(target.Y), MathHelper.Floor(target.Z), Tessellator.instance);
        GLManager.LightingEnabled = true;
        GLManager.ModelView.Pop();
    }
}
