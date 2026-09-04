using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
///     Squashes the model as a charge winds up and lets it swell back out afterwards — the ghast
///     drawing breath before it fires. The charge is read from whichever behavior keeps it, found by
///     capability rather than by knowing which mob this is.
/// </summary>
public sealed class ChargingEntityRenderer(ModelBase main, float shadowRadius) : LivingEntityRenderer(main, shadowRadius)
{
    protected override void PreRenderCallback(EntityLiving entity, float tickDelta)
    {
        var attack = entity.Behaviors.Find<FireballAttackBehavior>();
        if (attack is null) return;

        // Near zero charge this is ~1 and the model is drawn square; at full charge it falls away,
        // stretching the model tall and thin.
        var progress = attack.ChargeProgress(entity, tickDelta);
        var squash = 1.0F / (progress * progress * progress * progress * progress * 2.0F + 1.0F);

        GLManager.ModelView.Scale((8.0F + 1.0F / squash) / 2.0F, (8.0F + squash) / 2.0F, (8.0F + 1.0F / squash) / 2.0F);
        GLManager.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
    }
}
