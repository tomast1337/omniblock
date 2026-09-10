using Microsoft.Extensions.Logging;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;
using Color = OmniBlock.Client.UI.Colors.Color;
using Exception = System.Exception;

namespace OmniBlock.Client.Rendering.Entities;

public class LivingEntityRenderer : EntityRenderer
{
    /// <summary>Exception messages already logged, so an unported renderer doesn't relog every frame.</summary>
    private static readonly HashSet<string> s_reportedErrors = [];

    private readonly ILogger<LivingEntityRenderer> _logger = Log.Instance.For<LivingEntityRenderer>();

    protected ModelBase Main;
    protected ModelBase renderPassModel;

    public LivingEntityRenderer(ModelBase main, float shadowRadius)
    {
        Main = main;
        ShadowRadius = shadowRadius;
    }

    public void setRenderPassModel(ModelBase model) => renderPassModel = model;

    public virtual void DoRenderLiving(EntityLiving entity, double x, double y, double z, float yaw, float tickDelta)
    {
        RenderSystem.ModelView.Push();

        // Establishes the state the whole of this method and its passes assume, rather than
        // switching culling off and leaving everything else to whatever drew last.
        RenderSystem.State.Apply(RenderState.Entity);
        Main.OnGround = func_167_c(entity, tickDelta);
        if (renderPassModel != null)
        {
            renderPassModel.OnGround = Main.OnGround;
        }

        Main.IsRiding = entity.HasVehicle;
        if (renderPassModel != null)
        {
            renderPassModel.IsRiding = Main.IsRiding;
        }

        try
        {
            var bodyYaw = entity.LastBodyYaw + (entity.BodyYaw - entity.LastBodyYaw) * tickDelta;
            var headYaw = entity.PrevYaw + (entity.Yaw - entity.PrevYaw) * tickDelta;
            var pitch = entity.PrevPitch + (entity.Pitch - entity.PrevPitch) * tickDelta;
            Func_22012_b(entity, x, y, z);
            var animationProgress = getAnimationProgress(entity, tickDelta);
            RotateCorpse(entity, animationProgress, bodyYaw, tickDelta);
            var modelScale = 1.0F / 16.0F;
            RenderSystem.ModelView.Scale(-1.0F, -1.0F, 1.0F);
            PreRenderCallback(entity, tickDelta);
            RenderSystem.ModelView.Translate(0.0F, -24.0F * modelScale - 1 / 128f, 0.0F);
            var walkSpeed = entity.LastWalkAnimationSpeed + (entity.WalkAnimationSpeed - entity.LastWalkAnimationSpeed) * tickDelta;
            var walkPhase = entity.AnimationPhase - entity.WalkAnimationSpeed * (1.0F - tickDelta);
            if (walkSpeed > 1.0F)
            {
                walkSpeed = 1.0F;
            }

            LoadDownloadableImageTexture((entity as EntityPlayer)?.Name, entity.GetTexture());
            RenderSystem.AlphaTestEnabled = true;
            Main.SetLivingAnimations(entity, walkPhase, walkSpeed, tickDelta);
            Main.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);

            for (var renderPass = 0; renderPass < 4; ++renderPass)
            {
                if (ShouldRenderPass(entity, renderPass, tickDelta))
                {
                    renderPassModel.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);
                    RenderSystem.State.Apply(RenderState.Entity);
                    RenderSystem.AlphaTestEnabled = true;
                }
            }

            RenderMore(entity, tickDelta);
            var brightness = entity.GetBrightnessAtEyes(tickDelta);
            var colorMultiplier = getColorMultiplier(entity, brightness, tickDelta);
            if (((colorMultiplier >> 24) & 255) > 0 || entity.HurtTime > 0 || entity.DeathTime > 0)
            {
                EntityBatchRenderer.Instance.SetNoTexture();

                // Equal, not the usual LessOrEqual: the overlay is the same geometry drawn a second
                // time, so it must land on exactly the depths the body already wrote rather than in
                // front of them. Nothing is flushed to arrange that — the body was submitted first,
                // so its bucket is drawn first, and the overlay's is a separate bucket because the
                // depth comparison it carries differs.
                RenderSystem.TextureEnabled = false;
                RenderSystem.AlphaTestEnabled = false;
                RenderSystem.State.Apply(RenderState.Entity with
                {
                    Blend = BlendMode.Alpha,
                    DepthCompare = DepthCompare.Equal
                });
                if (entity.HurtTime > 0 || entity.DeathTime > 0)
                {
                    RenderSystem.Color = new Vector4D<float>(brightness, 0.0F, 0.0F, 0.4F);
                    Main.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);

                    for (var damagePass = 0; damagePass < 4; ++damagePass)
                    {
                        if (func_27005_b(entity, damagePass, tickDelta))
                        {
                            RenderSystem.Color = new Vector4D<float>(brightness, 0.0F, 0.0F, 0.4F);
                            renderPassModel.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);
                        }
                    }
                }

                if (((colorMultiplier >> 24) & 255) > 0)
                {
                    var red = ((colorMultiplier >> 16) & 255) / 255.0F;
                    var green = ((colorMultiplier >> 8) & 255) / 255.0F;
                    var blue = (colorMultiplier & 255) / 255.0F;
                    var alpha = ((colorMultiplier >> 24) & 255) / 255.0F;
                    RenderSystem.Color = new Vector4D<float>(red, green, blue, alpha);
                    Main.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);

                    for (var overlayPass = 0; overlayPass < 4; ++overlayPass)
                    {
                        if (func_27005_b(entity, overlayPass, tickDelta))
                        {
                            RenderSystem.Color = new Vector4D<float>(red, green, blue, alpha);
                            renderPassModel.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);
                        }
                    }
                }

                RenderSystem.State.Apply(RenderState.Entity);
                RenderSystem.AlphaTestEnabled = true;
                RenderSystem.TextureEnabled = true;
            }
        }
        catch (Exception e)
        {
            if (s_reportedErrors.Add(e.GetType().Name + e.Message))
            {
                _logger.LogError(e, e.Message);
            }
        }

        RenderSystem.ModelView.Pop();
        PassSpecialRender(entity, x, y, z);
    }

    protected virtual void Func_22012_b(EntityLiving entity, double x, double y, double z) => RenderSystem.ModelView.Translate((float)x, (float)y, (float)z);

    protected virtual void RotateCorpse(EntityLiving entity, float animationProgress, float bodyYaw, float tickDelta)
    {
        RenderSystem.ModelView.Rotate(180.0F - bodyYaw, 0.0F, 1.0F, 0.0F);
        if (entity.DeathTime > 0)
        {
            var deathRotation = (entity.DeathTime + tickDelta - 1.0F) / 20.0F * 1.6F;
            deathRotation = MathHelper.Sqrt(deathRotation);
            if (deathRotation > 1.0F)
            {
                deathRotation = 1.0F;
            }

            RenderSystem.ModelView.Rotate(deathRotation * getDeathMaxRotation(entity), 0.0F, 0.0F, 1.0F);
        }
    }

    protected float func_167_c(EntityLiving entity, float tickDelta) => entity.GetSwingProgress(tickDelta);

    protected virtual float getAnimationProgress(EntityLiving entity, float tickDelta) => entity.Age + tickDelta;

    protected virtual void RenderMore(EntityLiving entity, float tickDelta)
    {
    }

    protected virtual bool func_27005_b(EntityLiving entity, int renderPass, float tickDelta) => ShouldRenderPass(entity, renderPass, tickDelta);

    protected virtual bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta) => false;

    protected virtual float getDeathMaxRotation(EntityLiving entity) => 90.0F;

    protected virtual int getColorMultiplier(EntityLiving entity, float brightness, float tickDelta) => 0;

    protected virtual void PreRenderCallback(EntityLiving entity, float tickDelta)
    {
    }

    protected virtual void PassSpecialRender(EntityLiving entity, double x, double y, double z)
    {
        if (Dispatcher.Options.ShowDebugInfo)
        {
            renderLivingLabel(entity, entity.ID.ToString(), x, y, z, 64);
        }
    }

    protected void renderLivingLabel(EntityLiving entity, string label, double x, double y, double z, int maxDistance)
    {
        var distance = entity.GetDistance(Dispatcher.CameraEntity);
        if (distance <= maxDistance)
        {
            var fontRenderer = TextRenderer;
            var labelScale = 1.6F;
            var renderScale = (float)(1.0D / 60.0D) * labelScale;
            RenderSystem.ModelView.Push();
            RenderSystem.ModelView.Translate((float)x + 0.0F, (float)y + 2.3F, (float)z);
            RenderSystem.Normal = new Vector3D<float>(0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(-Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
            RenderSystem.ModelView.Scale(-renderScale, -renderScale, renderScale);
            // Drawn twice on purpose. This first pass ignores depth entirely, so the plate and the
            // text behind it show through whatever the label is standing in front of.
            RenderSystem.LightingEnabled = false;
            RenderSystem.State.Apply(RenderState.Entity with
            {
                Blend = BlendMode.Alpha,
                DepthTest = false,
                DepthWrite = false
            });
            var tessellator = Tessellator.instance;
            var yOffset = 0;
            if (label.Equals("deadmau5"))
            {
                yOffset = -10;
            }

            RenderSystem.TextureEnabled = false;
            tessellator.startDrawingQuads();
            var labelHalfWidth = fontRenderer.GetStringWidth(label) / 2;
            tessellator.setColorRGBA_F(0.0F, 0.0F, 0.0F, 0.25F);
            tessellator.addVertex(-labelHalfWidth - 1, -1 + yOffset, 0.0D);
            tessellator.addVertex(-labelHalfWidth - 1, 8 + yOffset, 0.0D);
            tessellator.addVertex(labelHalfWidth + 1, 8 + yOffset, 0.0D);
            tessellator.addVertex(labelHalfWidth + 1, -1 + yOffset, 0.0D);
            tessellator.draw(ProgramSlot.Basic);
            RenderSystem.TextureEnabled = true;
            fontRenderer.DrawString(label, -fontRenderer.GetStringWidth(label) / 2, yOffset, Color.WhiteAlpha20);
            // And again with depth restored, so the part of the label that is genuinely in front
            // draws solidly over the faint copy laid down above.
            RenderSystem.State.Apply(RenderState.Entity with
            {
                Blend = BlendMode.Alpha
            });
            fontRenderer.DrawString(label, -fontRenderer.GetStringWidth(label) / 2, yOffset, Color.WhiteAlpha20);
            RenderSystem.LightingEnabled = true;
            RenderSystem.State.Apply(RenderState.Entity);
            RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
            RenderSystem.ModelView.Pop();
        }
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => DoRenderLiving((EntityLiving)target, x, y, z, yaw, tickDelta);
}
