using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Drives the model's animation from the entity's <see cref="FlapDescentBehavior" /> instead of
///     from a mob class. The behavior owns both the counters and the interpolation, so this renderer
///     only has to ask it for the current angle.
///     <para>
///         Found by capability, not by reading a slot: the slot may hold a composite sharing the
///         wings with the mob's other physics.
///     </para>
/// </summary>
public sealed class FlappingEntityRenderer(ModelBase main, float shadowRadius) : LivingEntityRenderer(main, shadowRadius)
{
    protected override float getAnimationProgress(EntityLiving entity, float tickDelta) =>
        entity.Behaviors.Find<FlapDescentBehavior>() is { } flapping
            ? flapping.WingRotation(entity, tickDelta)
            : base.getAnimationProgress(entity, tickDelta);
}
