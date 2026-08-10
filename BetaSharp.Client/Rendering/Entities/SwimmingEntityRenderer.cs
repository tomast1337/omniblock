using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
///     Draws a mob that swims rather than walks: the model is oriented by its own tilt and beat
///     instead of by a walk cycle, and the animation it is posed with is the tentacle spread. Every
///     value comes from the swim behavior, found by capability rather than by knowing this is a
///     squid.
/// </summary>
public sealed class SwimmingEntityRenderer(ModelBase main, float shadowRadius) : LivingEntityRenderer(main, shadowRadius)
{
    protected override float getAnimationProgress(EntityLiving entity, float tickDelta) =>
        entity.Behaviors.Find<JetSwimBehavior>()?.TentacleSpread(entity, tickDelta) ?? 0.0F;

    protected override void RotateCorpse(EntityLiving entity, float deathTime, float bodyYaw, float tickDelta)
    {
        JetSwimBehavior? swim = entity.Behaviors.Find<JetSwimBehavior>();
        if (swim is null) return;

        GLManager.ModelView.Translate(0.0F, 0.5F, 0.0F);
        GLManager.ModelView.Rotate(180.0F - bodyYaw, 0.0F, 1.0F, 0.0F);
        GLManager.ModelView.Rotate(swim.TiltAngle(entity, tickDelta), 1.0F, 0.0F, 0.0F);
        GLManager.ModelView.Rotate(swim.TentaclePhase(entity, tickDelta), 0.0F, 1.0F, 0.0F);
        GLManager.ModelView.Translate(0.0F, -1.2F, 0.0F);
    }
}
