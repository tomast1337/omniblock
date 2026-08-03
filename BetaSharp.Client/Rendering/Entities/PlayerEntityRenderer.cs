using BetaSharp.Blocks;
using BetaSharp.Client.Entities;
using BetaSharp.Client.Rendering.Blocks;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Items.Behaviors;
using BetaSharp.Util.Maths;
using Color = BetaSharp.Client.UI.Colors.Color;

namespace BetaSharp.Client.Rendering.Entities;

public class PlayerEntityRenderer : LivingEntityRenderer
{

    private readonly ModelBiped _modelBipedMain;
    private readonly ModelBiped _armorChestplate = new(1.0F);
    private readonly ModelBiped _armor = new(0.5F);
    private static readonly string[] s_armorFilenamePrefix = ["cloth", "chain", "iron", "diamond", "gold"];

    public PlayerEntityRenderer() : base(new ModelBiped(0.0F), 0.5F)
    {
        _modelBipedMain = (ModelBiped)Main;
    }

    protected bool SetArmorModel(EntityPlayer playerEntity, int renderPass, float tickDelta)
    {
        ItemStack armorStack = playerEntity.Inventory.ArmorItemBySlot(3 - renderPass);
        if (armorStack != null)
        {
            Item armorItem = armorStack.GetItem();
            if (armorItem.GetBehavior<ArmorBehavior>() is { } armor)
            {
                loadTexture("/armor/" + s_armorFilenamePrefix[armor.RenderIndex] + "_" + (renderPass == 2 ? 2 : 1) + ".png");
                ModelBiped armorModel = renderPass == 2 ? _modelBipedMain : _armorChestplate;
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
        ItemStack heldItem = playerEntity.Inventory.ItemInHand;
        _armorChestplate.Field1278I = _armor.Field1278I = _modelBipedMain.Field1278I = heldItem != null;
        _armorChestplate.IsSneak = _armor.IsSneak = _modelBipedMain.IsSneak = playerEntity.IsSneaking();
        double renderY = y - playerEntity.StandingEyeHeight;
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
            float nameScale = 1.6F;
            float renderScale = (float)(1.0D / 60.0D) * nameScale;
            float distance = playerEntity.GetDistance(Dispatcher.CameraEntity);
            float maxDistance = playerEntity.IsSneaking() ? 32.0F : 64.0F;
            if (distance < maxDistance)
            {
                string displayName = playerEntity.Name;
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
                    TextRenderer fontRenderer = TextRenderer;
                    GLManager.ModelView.Push();
                    GLManager.ModelView.Translate((float)x + 0.0F, (float)y + 2.3F, (float)z);
                    GLManager.GL.Normal3(0.0F, 1.0F, 0.0F);
                    GLManager.ModelView.Rotate(-Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
                    GLManager.ModelView.Rotate(Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
                    GLManager.ModelView.Scale(-renderScale, -renderScale, renderScale);
                    GLManager.GL.Disable(GLEnum.Lighting);
                    GLManager.ModelView.Translate(0.0F, 0.25F / renderScale, 0.0F);
                    // The plate behind the name is depth tested but does not write depth, so the
                    // text drawn over it a moment later is not rejected for being at the same
                    // distance.
                    GLManager.State.Apply(RenderState.Entity with
                    {
                        Blend = BlendMode.Alpha,
                        DepthWrite = false
                    });

                    Tessellator tessellator = Tessellator.instance;
                    GLManager.GL.Disable(GLEnum.Texture2D);
                    tessellator.startDrawingQuads();
                    int nameHalfWidth = fontRenderer.GetStringWidth(displayName) / 2;
                    tessellator.setColorRGBA_F(0.0F, 0.0F, 0.0F, 0.25F);
                    tessellator.addVertex(-nameHalfWidth - 1, -1.0D, 0.0D);
                    tessellator.addVertex(-nameHalfWidth - 1, 8.0D, 0.0D);
                    tessellator.addVertex(nameHalfWidth + 1, 8.0D, 0.0D);
                    tessellator.addVertex(nameHalfWidth + 1, -1.0D, 0.0D);
                    tessellator.draw();
                    GLManager.GL.Enable(GLEnum.Texture2D);
                    GLManager.State.Apply(RenderState.Entity with { Blend = BlendMode.Alpha });
                    fontRenderer.DrawString(displayName, -fontRenderer.GetStringWidth(displayName) / 2, 0, Color.WhiteAlpha20);
                    GLManager.GL.Enable(GLEnum.Lighting);
                    GLManager.State.Apply(RenderState.Entity);
                    GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
                    GLManager.ModelView.Pop();
                }
            }
        }

    }

    protected void RenderSpecials(EntityPlayer playerEntity, float tickDelta)
    {
        ItemStack helmetStack = playerEntity.Inventory.ArmorItemBySlot(3);
        if (helmetStack != null && helmetStack.GetItem().Id < 256)
        {
            GLManager.ModelView.Push();
            _modelBipedMain.BipedHead.Transform(1.0F / 16.0F);
            if (BlockRenderer.IsSideLit(Block.Blocks[helmetStack.ItemId].RenderType))
            {
                float helmetScale = 10.0F / 16.0F;
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
            for (int earIndex = 0; earIndex < 2; ++earIndex)
            {
                heldItemScale = playerEntity.PrevYaw + (playerEntity.Yaw - playerEntity.PrevYaw) * tickDelta - (playerEntity.LastBodyYaw + (playerEntity.BodyYaw - playerEntity.LastBodyYaw) * tickDelta);
                float headPitchDelta = playerEntity.PrevPitch + (playerEntity.Pitch - playerEntity.PrevPitch) * tickDelta;
                GLManager.ModelView.Push();
                GLManager.ModelView.Rotate(heldItemScale, 0.0F, 1.0F, 0.0F);
                GLManager.ModelView.Rotate(headPitchDelta, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Translate(6.0F / 16.0F * (earIndex * 2 - 1), 0.0F, 0.0F);
                GLManager.ModelView.Translate(0.0F, -(6.0F / 16.0F), 0.0F);
                GLManager.ModelView.Rotate(-headPitchDelta, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(-heldItemScale, 0.0F, 1.0F, 0.0F);
                float earScale = 4.0F / 3.0F;
                GLManager.ModelView.Scale(earScale, earScale, earScale);
                _modelBipedMain.RenderEars(1.0F / 16.0F);
                GLManager.ModelView.Pop();
            }
        }

        if (LoadDownloadableImageTexture(playerEntity.PlayerCloakUrl, null))
        {
            GLManager.ModelView.Push();
            GLManager.ModelView.Translate(0.0F, 0.0F, 2.0F / 16.0F);
            double capeOffsetX = playerEntity.PrevCapePos.X + (playerEntity.CapePos.X - playerEntity.PrevCapePos.X) * (double)tickDelta - (playerEntity.PrevX + (playerEntity.X - playerEntity.PrevX) * (double)tickDelta);
            double capeOffsetY = playerEntity.PrevCapePos.Y + (playerEntity.CapePos.Y - playerEntity.PrevCapePos.Y) * (double)tickDelta - (playerEntity.PrevY + (playerEntity.Y - playerEntity.PrevY) * (double)tickDelta);
            double capeOffsetZ = playerEntity.PrevCapePos.Z + (playerEntity.CapePos.Z - playerEntity.PrevCapePos.Z) * (double)tickDelta - (playerEntity.PrevZ + (playerEntity.Z - playerEntity.PrevZ) * (double)tickDelta);
            float bodyYaw = playerEntity.LastBodyYaw + (playerEntity.BodyYaw - playerEntity.LastBodyYaw) * tickDelta;
            double sinBodyYaw = (double)MathHelper.Sin(bodyYaw * (float)Math.PI / 180.0F);
            double cosBodyYaw = (double)-MathHelper.Cos(bodyYaw * (float)Math.PI / 180.0F);
            float capeLift = (float)capeOffsetY * 10.0F;
            if (capeLift < -6.0F)
            {
                capeLift = -6.0F;
            }

            if (capeLift > 32.0F)
            {
                capeLift = 32.0F;
            }

            float capeSwingForward = (float)(capeOffsetX * sinBodyYaw + capeOffsetZ * cosBodyYaw) * 100.0F;
            float capeSwingSide = (float)(capeOffsetX * cosBodyYaw - capeOffsetZ * sinBodyYaw) * 100.0F;
            if (capeSwingForward < 0.0F)
            {
                capeSwingForward = 0.0F;
            }

            float bobbingAmount = playerEntity.PrevStepBobbingAmount + (playerEntity.StepBobbingAmount - playerEntity.PrevStepBobbingAmount) * tickDelta;
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

        ItemStack heldItem = playerEntity.Inventory.ItemInHand;
        if (heldItem != null)
        {
            GLManager.ModelView.Push();
            _modelBipedMain.BipedRightArm.Transform(1.0F / 16.0F);
            GLManager.ModelView.Translate(-(1.0F / 16.0F), 7.0F / 16.0F, 1.0F / 16.0F);
            if (playerEntity.FishHook != null)
            {
                heldItem = new ItemStack(Item.ByName("stick"));
            }

            if (heldItem.ItemId < 256 && BlockRenderer.IsSideLit(Block.Blocks[heldItem.ItemId].RenderType))
            {
                heldItemScale = 0.5F;
                GLManager.ModelView.Translate(0.0F, 3.0F / 16.0F, -(5.0F / 16.0F));
                heldItemScale *= 12.0F / 16.0F;
                GLManager.ModelView.Rotate(20.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
                GLManager.ModelView.Scale(heldItemScale, -heldItemScale, heldItemScale);
            }
            else if (Item.Items[heldItem.ItemId].IsHandheld())
            {
                heldItemScale = 10.0F / 16.0F;
                if (Item.Items[heldItem.ItemId].IsHandheldRod())
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
        float scale = 15.0F / 16.0F;
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

    protected override void PassSpecialRender(EntityLiving entity, double x, double y, double z)
    {
        RenderName((EntityPlayer)entity, x, y, z);
    }

    protected override void PreRenderCallback(EntityLiving entity, float tickDelta)
    {
        func_186_b((EntityPlayer)entity, tickDelta);
    }

    protected override bool ShouldRenderPass(EntityLiving entity, int renderPass, float tickDelta)
    {
        return SetArmorModel((EntityPlayer)entity, renderPass, tickDelta);
    }

    protected override void RenderMore(EntityLiving entity, float tickDelta)
    {
        RenderSpecials((EntityPlayer)entity, tickDelta);
    }

    protected override void RotateCorpse(EntityLiving entity, float animationProgress, float bodyYaw, float tickDelta)
    {
        func_22017_a((EntityPlayer)entity, animationProgress, bodyYaw, tickDelta);
    }

    protected override void Func_22012_b(EntityLiving entity, double x, double y, double z)
    {
        func_22016_b((EntityPlayer)entity, x, y, z);
    }

    public override void DoRenderLiving(EntityLiving entity, double x, double y, double z, float yaw, float tickDelta)
    {
        RenderPlayer((EntityPlayer)entity, x, y, z, yaw, tickDelta);
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        RenderPlayer((EntityPlayer)target, x, y, z, yaw, tickDelta);
    }
}
