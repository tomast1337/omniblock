using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
///     Lays a self-lit overlay over the model that gets brighter as the surroundings get darker —
///     a spider's eyes in a cave. Reads nothing but the entity's own brightness, so it needs no
///     knowledge of which mob it is drawing.
/// </summary>
public sealed class GlowingEyesEntityRenderer : LivingEntityRenderer
{
    private readonly float _deathRotation;
    private readonly string _texture;

    public GlowingEyesEntityRenderer(ModelBase main, ModelBase overlay, float shadowRadius, string texture, float deathRotation)
        : base(main, shadowRadius)
    {
        _texture = texture;
        _deathRotation = deathRotation;
        setRenderPassModel(overlay);
    }

    protected override float getDeathMaxRotation(EntityLiving entity) => _deathRotation;

    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        if (renderPass != 0) return false;

        loadTexture(_texture);
        var alpha = (1.0F - entity.GetBrightnessAtEyes(1.0F)) * 0.5F;
        // The alpha test is a shader uniform rather than pipeline state, so it stays a separate
        // call. Depth writing stays on, as it was before: the overlay sits on the model it covers.
        GLManager.AlphaTestEnabled = false;
        GLManager.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        });
        GLManager.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, alpha);
        return true;
    }
}
