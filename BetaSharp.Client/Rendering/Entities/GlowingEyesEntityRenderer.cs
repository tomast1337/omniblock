using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Lays a self-lit overlay over the model that gets brighter as the surroundings get darker —
///     a spider's eyes in a cave. Reads nothing but the entity's own brightness, so it needs no
///     knowledge of which mob it is drawing.
/// </summary>
public sealed class GlowingEyesEntityRenderer : LivingEntityRenderer
{
    private readonly string _texture;
    private readonly float _deathRotation;

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
        float alpha = (1.0F - entity.GetBrightnessAtEyes(1.0F)) * 0.5F;
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.Disable(GLEnum.AlphaTest);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        GLManager.GL.Color4(1.0F, 1.0F, 1.0F, alpha);
        return true;
    }
}
