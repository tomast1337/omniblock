using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Draws a tameable pet, posing its model by mood: the animation value it hands the model is the
///     tail angle, which reads high when the mob is angry and droops as it takes damage.
/// </summary>
public sealed class TamedEntityRenderer(ModelBase main, float shadowRadius) : LivingEntityRenderer(main, shadowRadius)
{
    protected override float getAnimationProgress(EntityLiving entity, float tickDelta) =>
        entity.Behaviors.Find<TameableBehavior>()?.TailRotation(entity) ?? 0.0F;
}
