using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

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
        var squish = (entity.Behaviors.Find<HoppingBehavior>()?.Squish(entity, tickDelta) ?? 0.0F) / (size * 0.5F + 1.0F);

        // Squashing flattens and widens by the same factor, so the mob keeps its volume as it lands.
        var widen = 1.0F / (squish + 1.0F);
        RenderSystem.ModelView.Scale(widen * size, 1.0F / widen * size, widen * size);
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

            // Blended, but still writing depth. That is what the shell has always done rather than
            // a choice made here, and it is not RenderState.Translucent, which does not. The shell
            // is submitted under this state and drawn under it, which it was not until instances
            // started carrying the state they were posed with.
            RenderSystem.State.Apply(RenderState.Entity with
            {
                Blend = BlendMode.Alpha
            });
            return true;
        }

        if (renderPass == 1)
        {
            RenderSystem.State.Apply(RenderState.Entity);
            RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        }

        return false;
    }
}
