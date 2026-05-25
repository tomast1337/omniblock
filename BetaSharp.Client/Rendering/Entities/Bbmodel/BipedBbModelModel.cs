using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public class BipedBbModelModel : BbModelEntityModel
{
    public readonly ModelPart BipedBody;
    public readonly ModelPart BipedHead;
    public readonly ModelPart BipedHeadwear;
    public readonly ModelPart BipedLeftArm;
    public readonly ModelPart BipedLeftLeg;
    public readonly ModelPart BipedRightArm;
    public readonly ModelPart BipedRightLeg;
    public readonly ModelPart BipedEars;
    public readonly ModelPart BipedCloak;
    public bool Field1278I;
    public bool Field1279H;

    public bool IsSneak;

    public BipedBbModelModel(float inflationOffset = 0f) : this("biped", inflationOffset)
    {
    }

    protected BipedBbModelModel(string entityId, float inflationOffset = 0f) : base(entityId, inflationOffset)
    {
        BipedHead = GetPart("bipedHead");
        BipedHeadwear = GetPart("bipedHeadwear");
        BipedBody = GetPart("bipedBody");
        BipedRightArm = GetPart("bipedRightArm");
        BipedLeftArm = GetPart("bipedLeftArm");
        BipedRightLeg = GetPart("bipedRightLeg");
        BipedLeftLeg = GetPart("bipedLeftLeg");
        BipedEars = new ModelPart(24, 0);
        BipedEars.addBox(-3.0f, -6.0f, -1.0f, 6, 6, 1, inflationOffset);
        BipedCloak = new ModelPart(0, 0);
        BipedCloak.addBox(-5.0f, 0.0f, -1.0f, 10, 16, 1, inflationOffset);
    }

    public void RenderEars(float scale)
    {
        BipedEars.rotateAngleY = BipedHead.rotateAngleY;
        BipedEars.rotateAngleX = BipedHead.rotateAngleX;
        BipedEars.rotationPointX = 0.0f;
        BipedEars.rotationPointY = 0.0f;
        BipedEars.render(scale);
    }

    public void RenderCloak(float scale)
    {
        BipedCloak.render(scale);
    }

    public override void setRotationAngles( float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        BipedHead.rotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        BipedHead.rotateAngleX = headPitch / (180.0f / MathF.PI);
        BipedHeadwear.rotateAngleY = BipedHead.rotateAngleY;
        BipedHeadwear.rotateAngleX = BipedHead.rotateAngleX;
        BipedRightArm.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 2.0f * limbSwingAmount * 0.5f;
        BipedLeftArm.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 2.0f * limbSwingAmount * 0.5f;
        BipedRightArm.rotateAngleZ = 0.0f;
        BipedLeftArm.rotateAngleZ = 0.0f;
        BipedRightLeg.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        BipedLeftLeg.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        BipedRightLeg.rotateAngleY = 0.0f;
        BipedLeftLeg.rotateAngleY = 0.0f;

        if (isRiding)
        {
            BipedRightArm.rotateAngleX += MathF.PI * -0.2f;
            BipedLeftArm.rotateAngleX += MathF.PI * -0.2f;
            BipedRightLeg.rotateAngleX = MathF.PI * -0.4f;
            BipedLeftLeg.rotateAngleX = MathF.PI * -0.4f;
            BipedRightLeg.rotateAngleY = MathF.PI * 0.1f;
            BipedLeftLeg.rotateAngleY = MathF.PI * -0.1f;
        }

        if (Field1279H)
        {
            BipedLeftArm.rotateAngleX = BipedLeftArm.rotateAngleX * 0.5f - MathF.PI * 0.1f;
        }

        if (Field1278I)
        {
            BipedRightArm.rotateAngleX = BipedRightArm.rotateAngleX * 0.5f - MathF.PI * 0.1f;
        }

        BipedRightArm.rotateAngleY = 0.0f;
        BipedLeftArm.rotateAngleY = 0.0f;

        if (onGround > -9990.0f)
        {
            float swingProgress = onGround;
            BipedBody.rotateAngleY = MathHelper.Sin(MathHelper.Sqrt(swingProgress) * MathF.PI * 2.0f) * 0.2f;
            BipedRightArm.rotationPointZ = MathHelper.Sin(BipedBody.rotateAngleY) * 5.0f;
            BipedRightArm.rotationPointX = -MathHelper.Cos(BipedBody.rotateAngleY) * 5.0f;
            BipedLeftArm.rotationPointZ = -MathHelper.Sin(BipedBody.rotateAngleY) * 5.0f;
            BipedLeftArm.rotationPointX = MathHelper.Cos(BipedBody.rotateAngleY) * 5.0f;
            BipedRightArm.rotateAngleY += BipedBody.rotateAngleY;
            BipedLeftArm.rotateAngleY += BipedBody.rotateAngleY;
            BipedLeftArm.rotateAngleX += BipedBody.rotateAngleY;
            swingProgress = 1.0f - onGround;
            swingProgress *= swingProgress;
            swingProgress *= swingProgress;
            swingProgress = 1.0f - swingProgress;
            float attackSwing = MathHelper.Sin(swingProgress * MathF.PI);
            float headOffset = MathHelper.Sin(onGround * MathF.PI) * -(BipedHead.rotateAngleX - 0.7f) * (12.0f / 16.0f);
            BipedRightArm.rotateAngleX -= (float)(attackSwing * 1.2d + headOffset);
            BipedRightArm.rotateAngleY += BipedBody.rotateAngleY * 2.0f;
            BipedRightArm.rotateAngleZ = MathHelper.Sin(onGround * MathF.PI) * -0.4f;
        }

        if (IsSneak)
        {
            BipedBody.rotateAngleX = 0.5f;
            BipedRightLeg.rotationPointZ = 4.0f;
            BipedLeftLeg.rotationPointZ = 4.0f;
            BipedRightLeg.rotationPointY = 9.0f;
            BipedLeftLeg.rotationPointY = 9.0f;
            BipedHead.rotationPointY = 1.0f;
        }
        else
        {
            BipedBody.rotateAngleX = 0.0f;
            BipedRightLeg.rotationPointZ = 0.0f;
            BipedLeftLeg.rotationPointZ = 0.0f;
            BipedRightLeg.rotationPointY = 12.0f;
            BipedLeftLeg.rotationPointY = 12.0f;
            BipedHead.rotationPointY = 0.0f;
        }

        BipedRightArm.rotateAngleZ += MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedLeftArm.rotateAngleZ -= MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedRightArm.rotateAngleX += MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
        BipedLeftArm.rotateAngleX -= MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
    }
}
