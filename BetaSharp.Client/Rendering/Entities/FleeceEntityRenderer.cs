using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Draws a fleece pass tinted by the entity's wool colour, skipping it once the fleece has been
///     sheared. Both facts come from the <see cref="WoolBehavior" /> in the entity's slots, so this
///     renderer never mentions a sheep.
/// </summary>
public sealed class FleeceEntityRenderer : LivingEntityRenderer
{
    private readonly string _texture;

    public FleeceEntityRenderer(ModelBase main, ModelBase fleece, float shadowRadius, string texture)
        : base(main, shadowRadius)
    {
        _texture = texture;
        setRenderPassModel(fleece);
    }

    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        if (renderPass != 0 || entity.Behaviors.Interactable is not WoolBehavior wool) return false;
        if (wool.IsShearedOn(entity)) return false;

        loadTexture(_texture);
        float brightness = entity.GetBrightnessAtEyes(tickDelta);
        float[] tint = WoolBehavior.ColorTable[wool.ColorOf(entity)];
        GLManager.GL.Color3(brightness * tint[0], brightness * tint[1], brightness * tint[2]);
        return true;
    }
}
