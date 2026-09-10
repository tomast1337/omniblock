using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

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
        if (renderPass != 0 || entity.Behaviors.Find<WoolBehavior>() is not { } wool) return false;
        if (wool.IsShearedOn(entity)) return false;

        loadTexture(_texture);
        var brightness = entity.GetBrightnessAtEyes(tickDelta);
        var tint = WoolBehavior.ColorTable[wool.ColorOf(entity)];
        RenderSystem.Color = new Vector4D<float>(brightness * tint[0], brightness * tint[1], brightness * tint[2], 1.0F);
        return true;
    }
}
