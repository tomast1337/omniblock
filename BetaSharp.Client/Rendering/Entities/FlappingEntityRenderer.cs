using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Drives the model's animation from a <see cref="FlapDescentBehavior" /> in the entity's
///     Physics slot instead of from a mob class. The behavior owns both the counters and the
///     interpolation, so this renderer only has to ask it for the current angle.
/// </summary>
public sealed class FlappingEntityRenderer(ModelBase main, float shadowRadius) : LivingEntityRenderer(main, shadowRadius)
{
    protected override float getAnimationProgress(EntityLiving entity, float tickDelta) =>
        entity.Behaviors.Physics is FlapDescentBehavior flapping
            ? flapping.WingRotation(entity, tickDelta)
            : base.getAnimationProgress(entity, tickDelta);
}
