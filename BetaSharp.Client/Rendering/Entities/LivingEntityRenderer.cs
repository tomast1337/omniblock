using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using Microsoft.Extensions.Logging;
using Color = BetaSharp.Client.UI.Colors.Color;
using Exception = System.Exception;

namespace BetaSharp.Client.Rendering.Entities;

public class LivingEntityRenderer : EntityRenderer
{

    protected ModelBase Main;
    protected ModelBase renderPassModel;
    private readonly ILogger<LivingEntityRenderer> _logger = Log.Instance.For<LivingEntityRenderer>();

    /// <summary>Exception messages already logged, so an unported renderer doesn't relog every frame.</summary>
    private static readonly HashSet<string> s_reportedErrors = [];

    public LivingEntityRenderer(ModelBase main, float shadowRadius)
    {
        this.Main = main;
        ShadowRadius = shadowRadius;
    }

    public void setRenderPassModel(ModelBase model)
    {
        renderPassModel = model;
    }

    public virtual void DoRenderLiving(EntityLiving entity, double x, double y, double z, float yaw, float tickDelta)
    {
        GLManager.ModelView.Push();

        // Establishes the state the whole of this method and its passes assume, rather than
        // switching culling off and leaving everything else to whatever drew last.
        GLManager.State.Apply(RenderState.Entity);
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
            float bodyYaw = entity.LastBodyYaw + (entity.BodyYaw - entity.LastBodyYaw) * tickDelta;
            float headYaw = entity.PrevYaw + (entity.Yaw - entity.PrevYaw) * tickDelta;
            float pitch = entity.PrevPitch + (entity.Pitch - entity.PrevPitch) * tickDelta;
            Func_22012_b(entity, x, y, z);
            float animationProgress = getAnimationProgress(entity, tickDelta);
            RotateCorpse(entity, animationProgress, bodyYaw, tickDelta);
            float modelScale = 1.0F / 16.0F;
            GLManager.ModelView.Scale(-1.0F, -1.0F, 1.0F);
            PreRenderCallback(entity, tickDelta);
            GLManager.ModelView.Translate(0.0F, -24.0F * modelScale - (1 / 128f), 0.0F);
            float walkSpeed = entity.LastWalkAnimationSpeed + (entity.WalkAnimationSpeed - entity.LastWalkAnimationSpeed) * tickDelta;
            float walkPhase = entity.AnimationPhase - entity.WalkAnimationSpeed * (1.0F - tickDelta);
            if (walkSpeed > 1.0F)
            {
                walkSpeed = 1.0F;
            }

            LoadDownloadableImageTexture((entity as EntityPlayer)?.Name, entity.GetTexture());
            GLManager.AlphaTestEnabled = true;
            Main.SetLivingAnimations(entity, walkPhase, walkSpeed, tickDelta);
            Main.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);

            for (int renderPass = 0; renderPass < 4; ++renderPass)
            {
                if (ShouldRenderPass(entity, renderPass, tickDelta))
                {
                    renderPassModel.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);
                    GLManager.State.Apply(RenderState.Entity);
                    GLManager.AlphaTestEnabled = true;
                }
            }

            RenderMore(entity, tickDelta);
            float brightness = entity.GetBrightnessAtEyes(tickDelta);
            int colorMultiplier = getColorMultiplier(entity, brightness, tickDelta);
            if ((colorMultiplier >> 24 & 255) > 0 || entity.HurtTime > 0 || entity.DeathTime > 0)
            {
                EntityBatchRenderer.Instance.SetNoTexture();

                // Equal, not the usual LessOrEqual: the overlay is the same geometry drawn a second
                // time, so it must land on exactly the depths the body already wrote rather than in
                // front of them. Nothing is flushed to arrange that — the body was submitted first,
                // so its bucket is drawn first, and the overlay's is a separate bucket because the
                // depth comparison it carries differs.
                GLManager.TextureEnabled = false;
                GLManager.AlphaTestEnabled = false;
                GLManager.State.Apply(RenderState.Entity with
                {
                    Blend = BlendMode.Alpha,
                    DepthCompare = DepthCompare.Equal
                });
                if (entity.HurtTime > 0 || entity.DeathTime > 0)
                {
                    GLManager.Color = new(brightness, 0.0F, 0.0F, 0.4F);
                    Main.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);

                    for (int damagePass = 0; damagePass < 4; ++damagePass)
                    {
                        if (func_27005_b(entity, damagePass, tickDelta))
                        {
                            GLManager.Color = new(brightness, 0.0F, 0.0F, 0.4F);
                            renderPassModel.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);
                        }
                    }
                }

                if ((colorMultiplier >> 24 & 255) > 0)
                {
                    float red = (colorMultiplier >> 16 & 255) / 255.0F;
                    float green = (colorMultiplier >> 8 & 255) / 255.0F;
                    float blue = (colorMultiplier & 255) / 255.0F;
                    float alpha = (colorMultiplier >> 24 & 255) / 255.0F;
                    GLManager.Color = new(red, green, blue, alpha);
                    Main.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);

                    for (int overlayPass = 0; overlayPass < 4; ++overlayPass)
                    {
                        if (func_27005_b(entity, overlayPass, tickDelta))
                        {
                            GLManager.Color = new(red, green, blue, alpha);
                            renderPassModel.Render(walkPhase, walkSpeed, animationProgress, headYaw - bodyYaw, pitch, modelScale);
                        }
                    }
                }

                GLManager.State.Apply(RenderState.Entity);
                GLManager.AlphaTestEnabled = true;
                GLManager.TextureEnabled = true;
            }

        }
        catch (Exception e)
        {
            if (s_reportedErrors.Add(e.GetType().Name + e.Message))
            {
                _logger.LogError(e, e.Message);
            }
        }

        GLManager.ModelView.Pop();
        PassSpecialRender(entity, x, y, z);
    }

    protected virtual void Func_22012_b(EntityLiving entity, double x, double y, double z)
    {
        GLManager.ModelView.Translate((float)x, (float)y, (float)z);
    }

    protected virtual void RotateCorpse(EntityLiving entity, float animationProgress, float bodyYaw, float tickDelta)
    {
        GLManager.ModelView.Rotate(180.0F - bodyYaw, 0.0F, 1.0F, 0.0F);
        if (entity.DeathTime > 0)
        {
            float deathRotation = (entity.DeathTime + tickDelta - 1.0F) / 20.0F * 1.6F;
            deathRotation = MathHelper.Sqrt(deathRotation);
            if (deathRotation > 1.0F)
            {
                deathRotation = 1.0F;
            }

            GLManager.ModelView.Rotate(deathRotation * getDeathMaxRotation(entity), 0.0F, 0.0F, 1.0F);
        }

    }

    protected float func_167_c(EntityLiving entity, float tickDelta)
    {
        return entity.GetSwingProgress(tickDelta);
    }

    protected virtual float getAnimationProgress(EntityLiving entity, float tickDelta)
    {
        return entity.Age + tickDelta;
    }

    protected virtual void RenderMore(EntityLiving entity, float tickDelta)
    {
    }

    protected virtual bool func_27005_b(EntityLiving entity, int renderPass, float tickDelta)
    {
        return ShouldRenderPass(entity, renderPass, tickDelta);
    }

    protected virtual bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        return false;
    }

    protected virtual float getDeathMaxRotation(EntityLiving entity)
    {
        return 90.0F;
    }

    protected virtual int getColorMultiplier(EntityLiving entity, float brightness, float tickDelta)
    {
        return 0;
    }

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
        float distance = entity.GetDistance(Dispatcher.CameraEntity);
        if (distance <= maxDistance)
        {
            TextRenderer fontRenderer = TextRenderer;
            float labelScale = 1.6F;
            float renderScale = (float)(1.0D / 60.0D) * labelScale;
            GLManager.ModelView.Push();
            GLManager.ModelView.Translate((float)x + 0.0F, (float)y + 2.3F, (float)z);
            GLManager.Normal = new(0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Rotate(-Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Rotate(Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
            GLManager.ModelView.Scale(-renderScale, -renderScale, renderScale);
            // Drawn twice on purpose. This first pass ignores depth entirely, so the plate and the
            // text behind it show through whatever the label is standing in front of.
            GLManager.LightingEnabled = false;
            GLManager.State.Apply(RenderState.Entity with
            {
                Blend = BlendMode.Alpha,
                DepthTest = false,
                DepthWrite = false
            });
            Tessellator tessellator = Tessellator.instance;
            int yOffset = 0;
            if (label.Equals("deadmau5"))
            {
                yOffset = -10;
            }

            GLManager.TextureEnabled = false;
            tessellator.startDrawingQuads();
            int labelHalfWidth = fontRenderer.GetStringWidth(label) / 2;
            tessellator.setColorRGBA_F(0.0F, 0.0F, 0.0F, 0.25F);
            tessellator.addVertex(-labelHalfWidth - 1, -1 + yOffset, 0.0D);
            tessellator.addVertex(-labelHalfWidth - 1, 8 + yOffset, 0.0D);
            tessellator.addVertex(labelHalfWidth + 1, 8 + yOffset, 0.0D);
            tessellator.addVertex(labelHalfWidth + 1, -1 + yOffset, 0.0D);
            tessellator.draw(ProgramSlot.Basic);
            GLManager.TextureEnabled = true;
            fontRenderer.DrawString(label, -fontRenderer.GetStringWidth(label) / 2, yOffset, Color.WhiteAlpha20);
            // And again with depth restored, so the part of the label that is genuinely in front
            // draws solidly over the faint copy laid down above.
            GLManager.State.Apply(RenderState.Entity with { Blend = BlendMode.Alpha });
            fontRenderer.DrawString(label, -fontRenderer.GetStringWidth(label) / 2, yOffset, Color.WhiteAlpha20);
            GLManager.LightingEnabled = true;
            GLManager.State.Apply(RenderState.Entity);
            GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
            GLManager.ModelView.Pop();
        }
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        DoRenderLiving((EntityLiving)target, x, y, z, yaw, tickDelta);
    }
}
