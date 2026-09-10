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

        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate((float)x, (float)y, (float)z);
        loadTexture("/terrain.png");
        var block = World.Content.Blocks.GetByProtocolId(blockId);
        var world = target.World;
        RenderSystem.LightingEnabled = false;
        BlockRenderer.RenderBlockFallingSand(block, world, MathHelper.Floor(target.X), MathHelper.Floor(target.Y), MathHelper.Floor(target.Z), Tessellator.instance);
        RenderSystem.LightingEnabled = true;
        RenderSystem.ModelView.Pop();
    }
}
