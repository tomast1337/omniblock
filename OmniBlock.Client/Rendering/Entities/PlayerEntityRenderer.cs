using OmniBlock.Blocks;
using OmniBlock.Client.Entities;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.Rendering.Entities;

public class PlayerEntityRenderer : LivingEntityRenderer
{
    private readonly ModelBiped _armor = new(0.5F);
    private readonly ModelBiped _armorChestplate = new(1.0F);

    private readonly ModelBiped _modelBipedMain;

    public PlayerEntityRenderer() : base(new ModelBiped(), 0.5F) => _modelBipedMain = (ModelBiped)Main;

    protected bool SetArmorModel(EntityPlayer playerEntity, int renderPass, float tickDelta)
    {
        var armorStack = playerEntity.Inventory.ArmorItemBySlot(3 - renderPass);
        if (armorStack != null)
        {
            var armorItem = armorStack.GetItem();
            if (armorItem.GetBehavior<ArmorBehavior>() is { } armor)
            {
                loadTexture("/armor/" + armor.TexturePrefix + "_" + (renderPass == 2 ? 2 : 1) + ".png");
                var armorModel = renderPass == 2 ? _modelBipedMain : _armorChestplate;
                armorModel.BipedHead.Visible = renderPass == 0;
                armorModel.BipedHeadwear.Visible = renderPass == 0;
                armorModel.BipedBody.Visible = renderPass == 1 || renderPass == 2;
                armorModel.BipedRightArm.Visible = renderPass == 1;
                armorModel.BipedLeftArm.Visible = renderPass == 1;
                armorModel.BipedRightLeg.Visible = renderPass == 2 || renderPass == 3;
                armorModel.BipedLeftLeg.Visible = renderPass == 2 || renderPass == 3;
                setRenderPassModel(armorModel);
                return true;
            }
        }

        return false;
    }

    public void RenderPlayer(EntityPlayer playerEntity, double x, double y, double z, float yaw, float tickDelta)
    {
        var heldItem = playerEntity.Inventory.ItemInHand;
        _armorChestplate.Field1278I = _armor.Field1278I = _modelBipedMain.Field1278I = heldItem != null;
        _armorChestplate.IsSneak = _armor.IsSneak = _modelBipedMain.IsSneak = playerEntity.IsSneaking();
        var renderY = y - playerEntity.StandingEyeHeight;
        if (playerEntity.IsSneaking() && playerEntity is not ClientPlayerEntity)
        {
            renderY -= 0.125D;
        }

        base.DoRenderLiving(playerEntity, x, renderY, z, yaw, tickDelta);
        _armorChestplate.IsSneak = _armor.IsSneak = _modelBipedMain.IsSneak = false;
        _armorChestplate.Field1278I = _armor.Field1278I = _modelBipedMain.Field1278I = false;
    }

    protected void RenderName(EntityPlayer playerEntity, double x, double y, double z)
    {
        if (Dispatcher.Options.HideGUI && playerEntity != Dispatcher.CameraEntity)
        {
            var nameScale = 1.6F;
            var renderScale = (float)(1.0D / 60.0D) * nameScale;
            var distance = playerEntity.GetDistance(Dispatcher.CameraEntity);
            var maxDistance = playerEntity.IsSneaking() ? 32.0F : 64.0F;
            if (distance < maxDistance)
            {
                var displayName = playerEntity.Name;
                if (!playerEntity.IsSneaking())
                {
                    if (playerEntity.IsSleeping)
                    {
                        renderLivingLabel(playerEntity, displayName, x, y - 1.5D, z, 64);
                    }
                    else
                    {
                        renderLivingLabel(playerEntity, displayName, x, y, z, 64);
                    }
                }
                else
                {
                    var fontRenderer = TextRenderer;
                    GLManager.ModelView.Push();
                    GLManager.ModelView.Translate((float)x + 0.0F, (float)y + 2.3F, (float)z);
                    GLManager.Normal = new Vector3D<float>(0.0F, 1.0F, 0.0F);
                    GLManager.ModelView.Rotate(-Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
                    GLManager.ModelView.Rotate(Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
                    GLManager.ModelView.Scale(-renderScale, -renderScale, renderScale);
                    GLManager.LightingEnabled = false;
                    GLManager.ModelView.Translate(0.0F, 0.25F / renderScale, 0.0F);
                    // The plate behind the name is depth tested but does not write depth, so the
                    // text drawn over it a moment later is not rejected for being at the same
                    // distance.
                    GLManager.State.Apply(RenderState.Entity with
                    {
                        Blend = BlendMode.Alpha,
                        DepthWrite = false
                    });

                    var tessellator = Tessellator.instance;
                    GLManager.TextureEnabled = false;
                    tessellator.startDrawingQuads();
                    var nameHalfWidth = fontRenderer.GetStringWidth(displayName) / 2;
                    tessellator.setColorRGBA_F(0.0F, 0.0F, 0.0F, 0.25F);
                    tessellator.addVertex(-nameHalfWidth - 1, -1.0D, 0.0D);
                    tessellator.addVertex(-nameHalfWidth - 1, 8.0D, 0.0D);
                    tessellator.addVertex(nameHalfWidth + 1, 8.0D, 0.0D);
                    tessellator.addVertex(nameHalfWidth + 1, -1.0D, 0.0D);
                    tessellator.draw(ProgramSlot.Basic);
                    GLManager.TextureEnabled = true;
                    GLManager.State.Apply(RenderState.Entity with
                    {
                        Blend = BlendMode.Alpha
                    });
                    fontRenderer.DrawString(displayName, -fontRenderer.GetStringWidth(displayName) / 2, 0, Color.WhiteAlpha20);
                    GLManager.LightingEnabled = true;
                    GLManager.State.Apply(RenderState.Entity);
                    GLManager.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
                    GLManager.ModelView.Pop();
                }
            }
        }
    }

    protected void RenderSpecials(EntityPlayer playerEntity, float tickDelta)
    {
        var helmetStack = playerEntity.Inventory.ArmorItemBySlot(3);
        if (helmetStack != null && helmetStack.GetItem().Id < 256)
        {
            GLManager.ModelView.Push();
            _modelBipedMain.BipedHead.Transform(1.0F / 16.0F);
            if (BlockRenderer.IsSideLit(global::OmniBlock.Registries.ContentRuntime.Current.Blocks.GetByProtocolId(helmetStack.ItemId).RenderType))
            {
                var helmetScale = 10.0F / 16.0F;
                GLManager.ModelView.Translate(0.0F, -0.25F, 0.0F);
                GLManager.ModelView.Rotate(180.0F, 0.0F, 1.0F, 0.0F);
                GLManager.ModelView.Scale(helmetScale, -helmetScale, helmetScale);
            }

            Dispatcher.HeldItemRenderer.renderItem(playerEntity, helmetStack);
            GLManager.ModelView.Pop();
        }

        float heldItemScale;
        if (playerEntity.Name.Equals("deadmau5") && LoadDownloadableImageTexture(playerEntity.Name, null))
        {
            for (var earIndex = 0; earIndex < 2; ++earIndex)
            {
                heldItemScale = playerEntity.PrevYaw + (playerEntity.Yaw - playerEntity.PrevYaw) * tickDelta - (playerEntity.LastBodyYaw + (playerEntity.BodyYaw - playerEntity.LastBodyYaw) * tickDelta);
                var headPitchDelta = playerEntity.PrevPitch + (playerEntity.Pitch - playerEntity.PrevPitch) * tickDelta;
                GLManager.ModelView.Push();
                GLManager.ModelView.Rotate(heldItemScale, 0.0F, 1.0F, 0.0F);
                GLManager.ModelView.Rotate(headPitchDelta, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Translate(6.0F / 16.0F * (earIndex * 2 - 1), 0.0F, 0.0F);
                GLManager.ModelView.Translate(0.0F, -(6.0F / 16.0F), 0.0F);
                GLManager.ModelView.Rotate(-headPitchDelta, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(-heldItemScale, 0.0F, 1.0F, 0.0F);
                var earScale = 4.0F / 3.0F;
                GLManager.ModelView.Scale(earScale, earScale, earScale);
                _modelBipedMain.RenderEars(1.0F / 16.0F);
                GLManager.ModelView.Pop();
            }
        }

        if (LoadDownloadableImageTexture(playerEntity.PlayerCloakUrl, null))
        {
            GLManager.ModelView.Push();
            GLManager.ModelView.Translate(0.0F, 0.0F, 2.0F / 16.0F);
            var capeOffsetX = playerEntity.PrevCapePos.X + (playerEntity.CapePos.X - playerEntity.PrevCapePos.X) * tickDelta - (playerEntity.PrevX + (playerEntity.X - playerEntity.PrevX) * tickDelta);
            var capeOffsetY = playerEntity.PrevCapePos.Y + (playerEntity.CapePos.Y - playerEntity.PrevCapePos.Y) * tickDelta - (playerEntity.PrevY + (playerEntity.Y - playerEntity.PrevY) * tickDelta);
            var capeOffsetZ = playerEntity.PrevCapePos.Z + (playerEntity.CapePos.Z - playerEntity.PrevCapePos.Z) * tickDelta - (playerEntity.PrevZ + (playerEntity.Z - playerEntity.PrevZ) * tickDelta);
            var bodyYaw = playerEntity.LastBodyYaw + (playerEntity.BodyYaw - playerEntity.LastBodyYaw) * tickDelta;
            var sinBodyYaw = (double)MathHelper.Sin(bodyYaw * (float)Math.PI / 180.0F);
            var cosBodyYaw = (double)-MathHelper.Cos(bodyYaw * (float)Math.PI / 180.0F);
            var capeLift = (float)capeOffsetY * 10.0F;
            if (capeLift < -6.0F)
            {
                capeLift = -6.0F;
            }

            if (capeLift > 32.0F)
            {
                capeLift = 32.0F;
            }

            var capeSwingForward = (float)(capeOffsetX * sinBodyYaw + capeOffsetZ * cosBodyYaw) * 100.0F;
            var capeSwingSide = (float)(capeOffsetX * cosBodyYaw - capeOffsetZ * sinBodyYaw) * 100.0F;
            if (capeSwingForward < 0.0F)
            {
                capeSwingForward = 0.0F;
            }

            var bobbingAmount = playerEntity.PrevStepBobbingAmount + (playerEntity.StepBobbingAmount - playerEntity.PrevStepBobbingAmount) * tickDelta;
            capeLift += MathHelper.Sin((playerEntity.PrevHorizontalSpeed + (playerEntity.HorizontalSpeed - playerEntity.PrevHorizontalSpeed) * tickDelta) * 6.0F) * 32.0F * bobbingAmount;
            if (playerEntity.IsSneaking())
            {
                capeLift += 25.0F;
            }

            GLManager.ModelView.Rotate(6.0F + capeSwingForward / 2.0F + capeLift, 1.0F, 0.0F, 0.0F);
            GLManager.ModelView.Rotate(capeSwingSide / 2.0F, 0.0F, 0.0F, 1.0F);
            GLManager.ModelView.Rotate(-capeSwingSide / 2.0F, 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Rotate(180.0F, 0.0F, 1.0F, 0.0F);
            _modelBipedMain.RenderCloak(1.0F / 16.0F);
            GLManager.ModelView.Pop();
        }

        var heldItem = playerEntity.Inventory.ItemInHand;
        if (heldItem != null)
        {
            GLManager.ModelView.Push();
            _modelBipedMain.BipedRightArm.Transform(1.0F / 16.0F);
            GLManager.ModelView.Translate(-(1.0F / 16.0F), 7.0F / 16.0F, 1.0F / 16.0F);
            if (playerEntity.FishHook != null)
            {
                heldItem = new ItemStack(playerEntity.World.Content.Items.Get("omniblock:stick"));
            }

            if (heldItem.ItemId < 256 && BlockRenderer.IsSideLit(global::OmniBlock.Registries.ContentRuntime.Current.Blocks.GetByProtocolId(heldItem.ItemId).RenderType))
            {
                heldItemScale = 0.5F;
                GLManager.ModelView.Translate(0.0F, 3.0F / 16.0F, -(5.0F / 16.0F));
                heldItemScale *= 12.0F / 16.0F;
                GLManager.ModelView.Rotate(20.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
                GLManager.ModelView.Scale(heldItemScale, -heldItemScale, heldItemScale);
            }
            else if (heldItem.GetItem().IsHandheld())
            {
                heldItemScale = 10.0F / 16.0F;
                if (heldItem.GetItem().IsHandheldRod())
                {
                    GLManager.ModelView.Rotate(180.0F, 0.0F, 0.0F, 1.0F);
                    GLManager.ModelView.Translate(0.0F, -(2.0F / 16.0F), 0.0F);
                }

                GLManager.ModelView.Translate(0.0F, 3.0F / 16.0F, 0.0F);
                GLManager.ModelView.Scale(heldItemScale, -heldItemScale, heldItemScale);
                GLManager.ModelView.Rotate(-100.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
            }
            else
            {
                heldItemScale = 6.0F / 16.0F;
                GLManager.ModelView.Translate(0.25F, 3.0F / 16.0F, -(3.0F / 16.0F));
                GLManager.ModelView.Scale(heldItemScale, heldItemScale, heldItemScale);
                GLManager.ModelView.Rotate(60.0F, 0.0F, 0.0F, 1.0F);
                GLManager.ModelView.Rotate(-90.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(20.0F, 0.0F, 0.0F, 1.0F);
            }

            Dispatcher.HeldItemRenderer.renderItem(playerEntity, heldItem);
            GLManager.ModelView.Pop();
        }
    }

    protected void func_186_b(EntityPlayer playerEntity, float tickDelta)
    {
        var scale = 15.0F / 16.0F;
        GLManager.ModelView.Scale(scale, scale, scale);
    }

    public void DrawFirstPersonHand()
    {
        _modelBipedMain.OnGround = 0.0F;
        _modelBipedMain.SetRotationAngles(0.0F, 0.0F, 0.0F, 0.0F, 0.0F, 1.0F / 16.0F);
        _modelBipedMain.BipedRightArm.Render(1.0F / 16.0F);
    }

    protected void func_22016_b(EntityPlayer playerEntity, double x, double y, double z)
    {
        if (playerEntity.IsAlive && playerEntity.IsSleeping)
        {
            base.Func_22012_b(playerEntity, x + playerEntity.SleepOffsetX, y + playerEntity.SleepOffsetY, z + playerEntity.SleepOffsetZ);
        }
        else
        {
            base.Func_22012_b(playerEntity, x, y, z);
        }
    }

    protected void func_22017_a(EntityPlayer playerEntity, float animationProgress, float bodyYaw, float tickDelta)
    {
        if (playerEntity.IsAlive && playerEntity.IsSleeping)
        {
            GLManager.ModelView.Rotate(playerEntity.GetSleepingRotation(), 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Rotate(getDeathMaxRotation(playerEntity), 0.0F, 0.0F, 1.0F);
            GLManager.ModelView.Rotate(270.0F, 0.0F, 1.0F, 0.0F);
        }
        else
        {
            base.RotateCorpse(playerEntity, animationProgress, bodyYaw, tickDelta);
        }
    }

    protected override void PassSpecialRender(EntityLiving entity, double x, double y, double z) => RenderName((EntityPlayer)entity, x, y, z);

    protected override void PreRenderCallback(EntityLiving entity, float tickDelta) => func_186_b((EntityPlayer)entity, tickDelta);

    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta) => SetArmorModel((EntityPlayer)entity, renderPass, tickDelta);

    protected override void RenderMore(EntityLiving entity, float tickDelta) => RenderSpecials((EntityPlayer)entity, tickDelta);

    protected override void RotateCorpse(EntityLiving entity, float animationProgress, float bodyYaw, float tickDelta) => func_22017_a((EntityPlayer)entity, animationProgress, bodyYaw, tickDelta);

    protected override void Func_22012_b(EntityLiving entity, double x, double y, double z) => func_22016_b((EntityPlayer)entity, x, y, z);

    public override void DoRenderLiving(EntityLiving entity, double x, double y, double z, float yaw, float tickDelta) => RenderPlayer((EntityPlayer)entity, x, y, z, yaw, tickDelta);

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => RenderPlayer((EntityPlayer)target, x, y, z, yaw, tickDelta);
}
