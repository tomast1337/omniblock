using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
///     Swells and flashes an entity in step with its <see cref="FuseBehavior" />, and lays a
///     scrolling charge overlay over anything whose declared power property is set. Both facts come
///     out of the entity's slots, so this renderer never names a creeper.
/// </summary>
public sealed class FuseEntityRenderer : LivingEntityRenderer
{
    private readonly ModelBase _overlay;
    private readonly string _overlayTexture;
    private readonly string _overlayProperty;

    public FuseEntityRenderer(ModelBase main, ModelBase overlay, float shadowRadius, string overlayTexture, string overlayProperty)
        : base(main, shadowRadius)
    {
        _overlay = overlay;
        _overlayTexture = overlayTexture;
        _overlayProperty = overlayProperty;
    }

    private static FuseBehavior? Fuse(EntityLiving entity) => entity.Behaviors.Find<FuseBehavior>();

    protected override void PreRenderCallback(EntityLiving entity, float partialTick)
    {
        if (Fuse(entity) is not { } fuse) return;

        float progress = fuse.FlashTime(entity, partialTick);
        float pulse = 1.0F + MathHelper.Sin(progress * 100.0F) * progress * 0.01F;

        progress = Math.Clamp(progress, 0.0F, 1.0F);
        progress *= progress;
        progress *= progress;

        float scaleX = (1.0F + progress * 0.4F) * pulse;
        float scaleY = (1.0F + progress * 0.1F) / pulse;
        GLManager.GL.Scale(scaleX, scaleY, scaleX);
    }

    protected override int getColorMultiplier(EntityLiving entity, float brightness, float tickDelta)
    {
        if (Fuse(entity) is not { } fuse) return 0;

        float progress = fuse.FlashTime(entity, tickDelta);
        if ((int)(progress * 10.0F) % 2 == 0) return 0;

        int alpha = Math.Clamp((int)(progress * 0.2F * 255.0F), 0, 255);
        return (alpha << 24) | (255 << 16) | (255 << 8) | 255;
    }

    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        if (entity.Synced<bool>(_overlayProperty) is not { Value: true }) return false;

        if (renderPass == 1)
        {
            float animationTime = entity.Age + tickDelta;
            loadTexture(_overlayTexture);
            GLManager.GL.MatrixMode(GLEnum.Texture);
            GLManager.GL.LoadIdentity();
            GLManager.GL.Translate(animationTime * 0.01F, animationTime * 0.01F, 0.0F);
            setRenderPassModel(_overlay);
            GLManager.GL.MatrixMode(GLEnum.Modelview);

            // Same reason the slime's shell does it: an instanced submission is drawn when the pass
            // ends, by which point this blending is gone, and an additive glow that is not blended
            // is just a grey shell.
            EntityInstanceBatchRenderer.Instance.ForceLegacyPath = true;
            EntityInstanceBatchRenderer.Instance.Flush();

            GLManager.State.Apply(RenderState.Entity with { Blend = BlendMode.Additive });
            GLManager.GL.Color4(0.5F, 0.5F, 0.5F, 1.0F);
            GLManager.GL.Disable(GLEnum.Lighting);
            return true;
        }

        if (renderPass == 2)
        {
            GLManager.GL.MatrixMode(GLEnum.Texture);
            GLManager.GL.LoadIdentity();
            GLManager.GL.MatrixMode(GLEnum.Modelview);
            EntityInstanceBatchRenderer.Instance.ForceLegacyPath = false;
            GLManager.GL.Enable(GLEnum.Lighting);
            GLManager.State.Apply(RenderState.Entity);
        }

        return false;
    }
}
