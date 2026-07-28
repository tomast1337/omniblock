using BetaSharp.Blocks;
using BetaSharp.Client.Rendering.Blocks;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Draws a falling block as the block it carries, read through
///     <see cref="SettleAsBlockBehavior" /> rather than an entity class.
/// </summary>
public class FallingBlockEntityRenderer : EntityRenderer
{
    public FallingBlockEntityRenderer(float shadowRadius)
    {
        ShadowRadius = shadowRadius;
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        int blockId = target.Behaviors.Find<SettleAsBlockBehavior>()?.BlockId(target) ?? 0;
        if (blockId == 0) return;

        GLManager.GL.PushMatrix();
        GLManager.GL.Translate((float)x, (float)y, (float)z);
        loadTexture("/terrain.png");
        Block block = Block.Blocks[blockId];
        IWorldContext world = target.World;
        GLManager.GL.Disable(GLEnum.Lighting);
        BlockRenderer.RenderBlockFallingSand(block, world, MathHelper.Floor(target.X), MathHelper.Floor(target.Y), MathHelper.Floor(target.Z), Tessellator.instance);
        GLManager.GL.Enable(GLEnum.Lighting);
        GLManager.GL.PopMatrix();
    }
}
