using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class WolfBbModelModel : BbModelEntityModel
{
    private readonly ModelPart _wolfBody;
    private readonly ModelPart _wolfHeadMain;
    private readonly ModelPart _wolfLeftEar;
    private readonly ModelPart _wolfLeg1;
    private readonly ModelPart _wolfLeg2;
    private readonly ModelPart _wolfLeg3;
    private readonly ModelPart _wolfLeg4;
    private readonly ModelPart _wolfMane;
    private readonly ModelPart _wolfRightEar;
    private readonly ModelPart _wolfSnout;
    private readonly ModelPart _wolfTail;

    public WolfBbModelModel() : base("wolf")
    {
        _wolfHeadMain = GetPart("wolfHeadMain");
        _wolfBody = GetPart("wolfBody");
        _wolfMane = GetPart("wolfMane");
        _wolfLeg1 = GetPart("wolfLeg1");
        _wolfLeg2 = GetPart("wolfLeg2");
        _wolfLeg3 = GetPart("wolfLeg3");
        _wolfLeg4 = GetPart("wolfLeg4");
        _wolfTail = GetPart("wolfTail");
        _wolfRightEar = GetPart("wolfRightEar");
        _wolfLeftEar = GetPart("wolfLeftEar");
        _wolfSnout = GetPart("wolfSnout");
    }

    public override void setLivingAnimations(EntityLiving entity, float limbSwing, float limbSwingAmount, float partialTick)
    {
        EntityWolf wolf = (EntityWolf)entity;

        if (wolf.IsWolfAngry)
        {
            _wolfTail.rotateAngleY = 0.0f;
        }
        else
        {
            _wolfTail.rotateAngleY = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        }

        if (wolf.IsWolfSitting)
        {
            _wolfMane.setRotationPoint(-1.0f, 16.0f, -3.0f);
            _wolfMane.rotateAngleX = MathF.PI * 0.4f;
            _wolfMane.rotateAngleY = 0.0f;
            _wolfBody.setRotationPoint(0.0f, 18.0f, 0.0f);
            _wolfBody.rotateAngleX = MathF.PI * 0.25f;
            _wolfTail.setRotationPoint(-1.0f, 21.0f, 6.0f);
            _wolfLeg1.setRotationPoint(-2.5f, 22.0f, 2.0f);
            _wolfLeg1.rotateAngleX = MathF.PI * 3.0f / 2.0f;
            _wolfLeg2.setRotationPoint(0.5f, 22.0f, 2.0f);
            _wolfLeg2.rotateAngleX = MathF.PI * 3.0f / 2.0f;
            _wolfLeg3.rotateAngleX = MathF.PI * 1.85f;
            _wolfLeg3.setRotationPoint(-2.49f, 17.0f, -4.0f);
            _wolfLeg4.rotateAngleX = MathF.PI * 1.85f;
            _wolfLeg4.setRotationPoint(0.51f, 17.0f, -4.0f);
        }
        else
        {
            _wolfBody.setRotationPoint(0.0f, 14.0f, 2.0f);
            _wolfBody.rotateAngleX = MathF.PI * 0.5f;
            _wolfMane.setRotationPoint(-1.0f, 14.0f, -3.0f);
            _wolfMane.rotateAngleX = _wolfBody.rotateAngleX;
            _wolfTail.setRotationPoint(-1.0f, 12.0f, 8.0f);
            _wolfLeg1.setRotationPoint(-2.5f, 16.0f, 7.0f);
            _wolfLeg2.setRotationPoint(0.5f, 16.0f, 7.0f);
            _wolfLeg3.setRotationPoint(-2.5f, 16.0f, -4.0f);
            _wolfLeg4.setRotationPoint(0.5f, 16.0f, -4.0f);
            _wolfLeg1.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
            _wolfLeg2.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
            _wolfLeg3.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
            _wolfLeg4.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        }

        float shakeAngle = wolf.getInterestedAngle(partialTick) + wolf.getShakeAngle(partialTick, 0.0f);
        _wolfHeadMain.rotateAngleZ = shakeAngle;
        _wolfRightEar.rotateAngleZ = shakeAngle;
        _wolfLeftEar.rotateAngleZ = shakeAngle;
        _wolfSnout.rotateAngleZ = shakeAngle;
        _wolfMane.rotateAngleZ = wolf.getShakeAngle(partialTick, -0.08f);
        _wolfBody.rotateAngleZ = wolf.getShakeAngle(partialTick, -0.16f);
        _wolfTail.rotateAngleZ = wolf.getShakeAngle(partialTick, -0.2f);

        if (wolf.getWolfShaking())
        {
            float shakeBrightness = wolf.GetBrightnessAtEyes(partialTick) * wolf.getShadingWhileShaking(partialTick);
            GLManager.GL.Color3(shakeBrightness, shakeBrightness, shakeBrightness);
        }
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float tailPitch, float netHeadYaw, float headPitch, float scale)
    {
        _wolfHeadMain.rotateAngleX = headPitch / (180.0f / MathF.PI);
        _wolfHeadMain.rotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _wolfRightEar.rotateAngleY = _wolfHeadMain.rotateAngleY;
        _wolfRightEar.rotateAngleX = _wolfHeadMain.rotateAngleX;
        _wolfLeftEar.rotateAngleY = _wolfHeadMain.rotateAngleY;
        _wolfLeftEar.rotateAngleX = _wolfHeadMain.rotateAngleX;
        _wolfSnout.rotateAngleY = _wolfHeadMain.rotateAngleY;
        _wolfSnout.rotateAngleX = _wolfHeadMain.rotateAngleX;
        _wolfTail.rotateAngleX = tailPitch;
    }
}
