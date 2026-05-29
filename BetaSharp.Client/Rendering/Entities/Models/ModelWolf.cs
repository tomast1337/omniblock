using BetaSharp.Client.Rendering.Core;
using BetaSharp.Entities;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelWolf : BbModelEntityModel
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

    public ModelWolf() : base("wolf")
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

    public override void SetLivingAnimations(EntityLiving entity, float limbSwing, float limbSwingAmount, float partialTick)
    {
        EntityWolf wolf = (EntityWolf)entity;

        if (wolf.IsWolfAngry)
        {
            _wolfTail.RotateAngleY = 0.0f;
        }
        else
        {
            _wolfTail.RotateAngleY = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        }

        if (wolf.IsWolfSitting)
        {
            _wolfMane.SetRotationPoint(-1.0f, 16.0f, -3.0f);
            _wolfMane.RotateAngleX = MathF.PI * 0.4f;
            _wolfMane.RotateAngleY = 0.0f;
            _wolfBody.SetRotationPoint(0.0f, 18.0f, 0.0f);
            _wolfBody.RotateAngleX = MathF.PI * 0.25f;
            _wolfTail.SetRotationPoint(-1.0f, 21.0f, 6.0f);
            _wolfLeg1.SetRotationPoint(-2.5f, 22.0f, 2.0f);
            _wolfLeg1.RotateAngleX = MathF.PI * 3.0f / 2.0f;
            _wolfLeg2.SetRotationPoint(0.5f, 22.0f, 2.0f);
            _wolfLeg2.RotateAngleX = MathF.PI * 3.0f / 2.0f;
            _wolfLeg3.RotateAngleX = MathF.PI * 1.85f;
            _wolfLeg3.SetRotationPoint(-2.49f, 17.0f, -4.0f);
            _wolfLeg4.RotateAngleX = MathF.PI * 1.85f;
            _wolfLeg4.SetRotationPoint(0.51f, 17.0f, -4.0f);
        }
        else
        {
            _wolfBody.SetRotationPoint(0.0f, 14.0f, 2.0f);
            _wolfBody.RotateAngleX = MathF.PI * 0.5f;
            _wolfMane.SetRotationPoint(-1.0f, 14.0f, -3.0f);
            _wolfMane.RotateAngleX = _wolfBody.RotateAngleX;
            _wolfTail.SetRotationPoint(-1.0f, 12.0f, 8.0f);
            _wolfLeg1.SetRotationPoint(-2.5f, 16.0f, 7.0f);
            _wolfLeg2.SetRotationPoint(0.5f, 16.0f, 7.0f);
            _wolfLeg3.SetRotationPoint(-2.5f, 16.0f, -4.0f);
            _wolfLeg4.SetRotationPoint(0.5f, 16.0f, -4.0f);
            _wolfLeg1.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
            _wolfLeg2.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
            _wolfLeg3.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
            _wolfLeg4.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        }

        float shakeAngle = wolf.getInterestedAngle(partialTick) + wolf.getShakeAngle(partialTick, 0.0f);
        _wolfHeadMain.RotateAngleZ = shakeAngle;
        _wolfRightEar.RotateAngleZ = shakeAngle;
        _wolfLeftEar.RotateAngleZ = shakeAngle;
        _wolfSnout.RotateAngleZ = shakeAngle;
        _wolfMane.RotateAngleZ = wolf.getShakeAngle(partialTick, -0.08f);
        _wolfBody.RotateAngleZ = wolf.getShakeAngle(partialTick, -0.16f);
        _wolfTail.RotateAngleZ = wolf.getShakeAngle(partialTick, -0.2f);

        if (wolf.getWolfShaking())
        {
            float shakeBrightness = wolf.GetBrightnessAtEyes(partialTick) * wolf.getShadingWhileShaking(partialTick);
            GLManager.GL.Color3(shakeBrightness, shakeBrightness, shakeBrightness);
        }
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float tailPitch, float netHeadYaw, float headPitch, float scale)
    {
        _wolfHeadMain.RotateAngleX = headPitch / (180.0f / MathF.PI);
        _wolfHeadMain.RotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _wolfRightEar.RotateAngleY = _wolfHeadMain.RotateAngleY;
        _wolfRightEar.RotateAngleX = _wolfHeadMain.RotateAngleX;
        _wolfLeftEar.RotateAngleY = _wolfHeadMain.RotateAngleY;
        _wolfLeftEar.RotateAngleX = _wolfHeadMain.RotateAngleX;
        _wolfSnout.RotateAngleY = _wolfHeadMain.RotateAngleY;
        _wolfSnout.RotateAngleX = _wolfHeadMain.RotateAngleX;
        _wolfTail.RotateAngleX = tailPitch;
    }
}
