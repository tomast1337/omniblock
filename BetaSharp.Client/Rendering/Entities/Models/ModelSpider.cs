using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelSpider : BbModelEntityModel
{
    private readonly ModelPart _spiderBody;
    private readonly ModelPart _spiderHead;
    private readonly ModelPart _spiderLeg1;
    private readonly ModelPart _spiderLeg2;
    private readonly ModelPart _spiderLeg3;
    private readonly ModelPart _spiderLeg4;
    private readonly ModelPart _spiderLeg5;
    private readonly ModelPart _spiderLeg6;
    private readonly ModelPart _spiderLeg7;
    private readonly ModelPart _spiderLeg8;
    private readonly ModelPart _spiderNeck;

    public ModelSpider() : base("spider")
    {
        _spiderHead = GetPart("spiderHead");
        _spiderNeck = GetPart("spiderNeck");
        _spiderBody = GetPart("spiderBody");
        _spiderLeg1 = GetPart("spiderLeg1");
        _spiderLeg2 = GetPart("spiderLeg2");
        _spiderLeg3 = GetPart("spiderLeg3");
        _spiderLeg4 = GetPart("spiderLeg4");
        _spiderLeg5 = GetPart("spiderLeg5");
        _spiderLeg6 = GetPart("spiderLeg6");
        _spiderLeg7 = GetPart("spiderLeg7");
        _spiderLeg8 = GetPart("spiderLeg8");
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        _spiderHead.RotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _spiderHead.RotateAngleX = headPitch / (180.0f / MathF.PI);

        const float baseLegAngle = MathF.PI * 0.25f;
        _spiderLeg1.RotateAngleZ = -baseLegAngle;
        _spiderLeg2.RotateAngleZ = baseLegAngle;
        _spiderLeg3.RotateAngleZ = -baseLegAngle * 0.74f;
        _spiderLeg4.RotateAngleZ = baseLegAngle * 0.74f;
        _spiderLeg5.RotateAngleZ = -baseLegAngle * 0.74f;
        _spiderLeg6.RotateAngleZ = baseLegAngle * 0.74f;
        _spiderLeg7.RotateAngleZ = -baseLegAngle;
        _spiderLeg8.RotateAngleZ = baseLegAngle;

        const float yawSpread = MathF.PI * 0.125f;
        _spiderLeg1.RotateAngleY = yawSpread * 2.0f;
        _spiderLeg2.RotateAngleY = -yawSpread * 2.0f;
        _spiderLeg3.RotateAngleY = yawSpread;
        _spiderLeg4.RotateAngleY = -yawSpread;
        _spiderLeg5.RotateAngleY = -yawSpread;
        _spiderLeg6.RotateAngleY = yawSpread;
        _spiderLeg7.RotateAngleY = -yawSpread * 2.0f;
        _spiderLeg8.RotateAngleY = yawSpread * 2.0f;

        float frontLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f) * 0.4f) * limbSwingAmount;
        float midFrontLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f + MathF.PI) * 0.4f) * limbSwingAmount;
        float midBackLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f + MathF.PI * 0.5f) * 0.4f) * limbSwingAmount;
        float backLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f + MathF.PI * 1.5f) * 0.4f) * limbSwingAmount;
        float frontLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f) * 0.4f) * limbSwingAmount;
        float midFrontLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f + MathF.PI) * 0.4f) * limbSwingAmount;
        float midBackLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f + MathF.PI * 0.5f) * 0.4f) * limbSwingAmount;
        float backLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f + MathF.PI * 1.5f) * 0.4f) * limbSwingAmount;

        _spiderLeg1.RotateAngleY += frontLegSwing;
        _spiderLeg2.RotateAngleY += -frontLegSwing;
        _spiderLeg3.RotateAngleY += midFrontLegSwing;
        _spiderLeg4.RotateAngleY += -midFrontLegSwing;
        _spiderLeg5.RotateAngleY += midBackLegSwing;
        _spiderLeg6.RotateAngleY += -midBackLegSwing;
        _spiderLeg7.RotateAngleY += backLegSwing;
        _spiderLeg8.RotateAngleY += -backLegSwing;
        _spiderLeg1.RotateAngleZ += frontLegLift;
        _spiderLeg2.RotateAngleZ += -frontLegLift;
        _spiderLeg3.RotateAngleZ += midFrontLegLift;
        _spiderLeg4.RotateAngleZ += -midFrontLegLift;
        _spiderLeg5.RotateAngleZ += midBackLegLift;
        _spiderLeg6.RotateAngleZ += -midBackLegLift;
        _spiderLeg7.RotateAngleZ += backLegLift;
        _spiderLeg8.RotateAngleZ += -backLegLift;
    }
}
