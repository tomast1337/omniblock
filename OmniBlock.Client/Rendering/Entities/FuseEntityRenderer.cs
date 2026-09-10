using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
///     Swells and flashes an entity in step with its <see cref="FuseBehavior" />, and lays a
///     scrolling charge overlay over anything whose declared power property is set. Both facts come
///     out of the entity's slots, so this renderer never names a creeper.
/// </summary>
public sealed class FuseEntityRenderer : LivingEntityRenderer
{
    private readonly ModelBase _overlay;
    private readonly string _overlayProperty;
    private readonly string _overlayTexture;

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

        var progress = fuse.FlashTime(entity, partialTick);
        var pulse = 1.0F + MathHelper.Sin(progress * 100.0F) * progress * 0.01F;

        progress = Math.Clamp(progress, 0.0F, 1.0F);
        progress *= progress;
        progress *= progress;

        var scaleX = (1.0F + progress * 0.4F) * pulse;
        var scaleY = (1.0F + progress * 0.1F) / pulse;
        RenderSystem.ModelView.Scale(scaleX, scaleY, scaleX);
    }

    protected override int getColorMultiplier(EntityLiving entity, float brightness, float tickDelta)
    {
        if (Fuse(entity) is not { } fuse) return 0;

        var progress = fuse.FlashTime(entity, tickDelta);
        if ((int)(progress * 10.0F) % 2 == 0) return 0;

        var alpha = Math.Clamp((int)(progress * 0.2F * 255.0F), 0, 255);
        return (alpha << 24) | (255 << 16) | (255 << 8) | 255;
    }

    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        if (entity.Synced<bool>(_overlayProperty) is not { Value: true }) return false;

        if (renderPass == 1)
        {
            var animationTime = entity.Age + tickDelta;
            loadTexture(_overlayTexture);
            RenderSystem.TextureMatrix.LoadIdentity();
            RenderSystem.TextureMatrix.Translate(animationTime * 0.01F, animationTime * 0.01F, 0.0F);
            setRenderPassModel(_overlay);

            RenderSystem.State.Apply(RenderState.Entity with
            {
                Blend = BlendMode.Additive
            });
            RenderSystem.Color = new Vector4D<float>(0.5F, 0.5F, 0.5F, 1.0F);
            RenderSystem.LightingEnabled = false;
            return true;
        }

        if (renderPass == 2)
        {
            RenderSystem.TextureMatrix.LoadIdentity();
            RenderSystem.LightingEnabled = true;
            RenderSystem.State.Apply(RenderState.Entity);
        }

        return false;
    }
}
