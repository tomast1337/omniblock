using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
///     Draws a mob at a uniform multiple of its model size, with the shadow scaled to match. The
///     factor comes from the definition, so nothing here knows it is drawing a giant.
/// </summary>
public sealed class ScaledEntityRenderer(ModelBase main, float shadowRadius, float scale)
    : LivingEntityRenderer(main, shadowRadius * scale)
{
    protected override void PreRenderCallback(EntityLiving entity, float tickDelta) =>
        GLManager.ModelView.Scale(scale, scale, scale);
}
