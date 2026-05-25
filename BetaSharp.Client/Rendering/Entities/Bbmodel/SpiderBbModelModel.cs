using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class SpiderBbModelModel : BbModelEntityModel
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

    public SpiderBbModelModel() : base("spider")
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

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        _spiderHead.rotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _spiderHead.rotateAngleX = headPitch / (180.0f / MathF.PI);

        const float baseLegAngle = MathF.PI * 0.25f;
        _spiderLeg1.rotateAngleZ = -baseLegAngle;
        _spiderLeg2.rotateAngleZ = baseLegAngle;
        _spiderLeg3.rotateAngleZ = -baseLegAngle * 0.74f;
        _spiderLeg4.rotateAngleZ = baseLegAngle * 0.74f;
        _spiderLeg5.rotateAngleZ = -baseLegAngle * 0.74f;
        _spiderLeg6.rotateAngleZ = baseLegAngle * 0.74f;
        _spiderLeg7.rotateAngleZ = -baseLegAngle;
        _spiderLeg8.rotateAngleZ = baseLegAngle;

        const float yawSpread = MathF.PI * 0.125f;
        _spiderLeg1.rotateAngleY = yawSpread * 2.0f;
        _spiderLeg2.rotateAngleY = -yawSpread * 2.0f;
        _spiderLeg3.rotateAngleY = yawSpread;
        _spiderLeg4.rotateAngleY = -yawSpread;
        _spiderLeg5.rotateAngleY = -yawSpread;
        _spiderLeg6.rotateAngleY = yawSpread;
        _spiderLeg7.rotateAngleY = -yawSpread * 2.0f;
        _spiderLeg8.rotateAngleY = yawSpread * 2.0f;

        float frontLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f) * 0.4f) * limbSwingAmount;
        float midFrontLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f + MathF.PI) * 0.4f) * limbSwingAmount;
        float midBackLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f + MathF.PI * 0.5f) * 0.4f) * limbSwingAmount;
        float backLegSwing = -(MathHelper.Cos(limbSwing * 0.6662f * 2.0f + MathF.PI * 1.5f) * 0.4f) * limbSwingAmount;
        float frontLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f) * 0.4f) * limbSwingAmount;
        float midFrontLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f + MathF.PI) * 0.4f) * limbSwingAmount;
        float midBackLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f + MathF.PI * 0.5f) * 0.4f) * limbSwingAmount;
        float backLegLift = MathF.Abs(MathHelper.Sin(limbSwing * 0.6662f + MathF.PI * 1.5f) * 0.4f) * limbSwingAmount;

        _spiderLeg1.rotateAngleY += frontLegSwing;
        _spiderLeg2.rotateAngleY += -frontLegSwing;
        _spiderLeg3.rotateAngleY += midFrontLegSwing;
        _spiderLeg4.rotateAngleY += -midFrontLegSwing;
        _spiderLeg5.rotateAngleY += midBackLegSwing;
        _spiderLeg6.rotateAngleY += -midBackLegSwing;
        _spiderLeg7.rotateAngleY += backLegSwing;
        _spiderLeg8.rotateAngleY += -backLegSwing;
        _spiderLeg1.rotateAngleZ += frontLegLift;
        _spiderLeg2.rotateAngleZ += -frontLegLift;
        _spiderLeg3.rotateAngleZ += midFrontLegLift;
        _spiderLeg4.rotateAngleZ += -midFrontLegLift;
        _spiderLeg5.rotateAngleZ += midBackLegLift;
        _spiderLeg6.rotateAngleZ += -midBackLegLift;
        _spiderLeg7.rotateAngleZ += backLegLift;
        _spiderLeg8.rotateAngleZ += -backLegLift;
    }
}
