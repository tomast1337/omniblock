using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Draws a second pass over the main model when a declared synced property is set — a pig's
///     saddle. The property is named in the definition and read by name, so this renderer knows
///     nothing about which mob it is drawing.
/// </summary>
public sealed class OverlayEntityRenderer : LivingEntityRenderer
{
    private readonly string _property;
    private readonly string _texture;

    public OverlayEntityRenderer(ModelBase main, ModelBase overlay, float shadowRadius, string property, string texture)
        : base(main, shadowRadius)
    {
        _property = property;
        _texture = texture;
        setRenderPassModel(overlay);
    }

    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        loadTexture(_texture);
        return renderPass == 0 && entity.Synced<bool>(_property) is { Value: true };
    }
}
