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

    /// <summary>
    ///     The body is already drawn by the time this runs; pass 0 lays the translucent shell over
    ///     it and pass 1 puts the state back.
    /// </summary>
    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        if (renderPass == 0)
        {
            setRenderPassModel(shell);

            // The shell has to draw on the legacy path. An instanced submission is only drawn when
            // the pass ends, by which time the blending set up here has long since been turned off
            // again, so the shell came out solid. The legacy batch flushes whenever raster state
            // changes and therefore draws it under the state it was posed with.
            EntityInstanceBatchRenderer.Instance.ForceLegacyPath = true;

            // And the body has to be in the depth buffer before a translucent shell is drawn over
            // it, which it is not while it sits queued.
            EntityInstanceBatchRenderer.Instance.Flush();

            // Blended, but still writing depth. That is what the shell has always done rather than
            // a choice made here, and it is not RenderState.Translucent, which does not.
            GLManager.State.Apply(RenderState.Entity with { Blend = BlendMode.Alpha });
            return true;
        }

        if (renderPass == 1)
        {
            EntityInstanceBatchRenderer.Instance.ForceLegacyPath = false;
            GLManager.State.Apply(RenderState.Entity);
            GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
        }

        return false;
    }
}
