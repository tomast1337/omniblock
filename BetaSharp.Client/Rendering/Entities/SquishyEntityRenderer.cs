using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Draws a mob scaled by its declared <c>size</c> and squashed by how recently it landed, with a
///     translucent shell over the body. Both values are asked for rather than cast to: the size is a
///     synced property and the squash comes from whichever behavior keeps it.
/// </summary>
public sealed class SquishyEntityRenderer(ModelBase main, ModelBase shell, float shadowRadius)
    : LivingEntityRenderer(main, shadowRadius)
{
    protected override void PreRenderCallback(EntityLiving entity, float tickDelta)
    {
        int size = entity.Synced<byte>("size")?.Value ?? 1;
        float squish = (entity.Behaviors.Find<HoppingBehavior>()?.Squish(entity, tickDelta) ?? 0.0F) / (size * 0.5F + 1.0F);

        // Squashing flattens and widens by the same factor, so the mob keeps its volume as it lands.
        float widen = 1.0F / (squish + 1.0F);
        GLManager.GL.Scale(widen * size, 1.0F / widen * size, widen * size);
    }

    /// <summary>Shell first with blending on, then the body opaque over it.</summary>
    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        if (renderPass == 0)
        {
            setRenderPassModel(shell);

            // Blended, but still writing depth. That is what the shell has always done rather than
            // a choice made here, and it is not RenderState.Translucent, which does not.
            GLManager.State.ApplyUntrusted(RenderState.Opaque with { Blend = BlendMode.Alpha });
            return true;
        }

        if (renderPass == 1)
        {
            GLManager.State.ApplyUntrusted(RenderState.Opaque);
            GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
        }

        return false;
    }
}
