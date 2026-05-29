using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public class ModelBiped : BbModelEntityModel
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

    public ModelBiped(float inflationOffset = 0f) : this("biped", inflationOffset)
    {
    }

    protected ModelBiped(string entityId, float inflationOffset = 0f) : base(entityId, inflationOffset)
    {
        BipedHead = GetPart("bipedHead");
        BipedHeadwear = GetPart("bipedHeadwear");
        BipedBody = GetPart("bipedBody");
        BipedRightArm = GetPart("bipedRightArm");
        BipedLeftArm = GetPart("bipedLeftArm");
        BipedRightLeg = GetPart("bipedRightLeg");
        BipedLeftLeg = GetPart("bipedLeftLeg");
        BipedEars = new ModelPart(24, 0);
        BipedEars.AddBox(-3.0f, -6.0f, -1.0f, 6, 6, 1, inflationOffset);
        BipedCloak = new ModelPart(0, 0);
        BipedCloak.AddBox(-5.0f, 0.0f, -1.0f, 10, 16, 1, inflationOffset);
    }

    public void RenderEars(float scale)
    {
        BipedEars.RotateAngleY = BipedHead.RotateAngleY;
        BipedEars.RotateAngleX = BipedHead.RotateAngleX;
        BipedEars.RotationPointX = 0.0f;
        BipedEars.RotationPointY = 0.0f;
        BipedEars.Render(scale);
    }

    public void RenderCloak(float scale)
    {
        BipedCloak.Render(scale);
    }

    public override void SetRotationAngles( float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        BipedHead.RotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        BipedHead.RotateAngleX = headPitch / (180.0f / MathF.PI);
        BipedHeadwear.RotateAngleY = BipedHead.RotateAngleY;
        BipedHeadwear.RotateAngleX = BipedHead.RotateAngleX;
        BipedRightArm.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 2.0f * limbSwingAmount * 0.5f;
        BipedLeftArm.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 2.0f * limbSwingAmount * 0.5f;
        BipedRightArm.RotateAngleZ = 0.0f;
        BipedLeftArm.RotateAngleZ = 0.0f;
        BipedRightLeg.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        BipedLeftLeg.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        BipedRightLeg.RotateAngleY = 0.0f;
        BipedLeftLeg.RotateAngleY = 0.0f;

        if (IsRiding)
        {
            BipedRightArm.RotateAngleX += MathF.PI * -0.2f;
            BipedLeftArm.RotateAngleX += MathF.PI * -0.2f;
            BipedRightLeg.RotateAngleX = MathF.PI * -0.4f;
            BipedLeftLeg.RotateAngleX = MathF.PI * -0.4f;
            BipedRightLeg.RotateAngleY = MathF.PI * 0.1f;
            BipedLeftLeg.RotateAngleY = MathF.PI * -0.1f;
        }

        if (Field1279H)
        {
            BipedLeftArm.RotateAngleX = BipedLeftArm.RotateAngleX * 0.5f - MathF.PI * 0.1f;
        }

        if (Field1278I)
        {
            BipedRightArm.RotateAngleX = BipedRightArm.RotateAngleX * 0.5f - MathF.PI * 0.1f;
        }

        BipedRightArm.RotateAngleY = 0.0f;
        BipedLeftArm.RotateAngleY = 0.0f;

        if (OnGround > -9990.0f)
        {
            float swingProgress = OnGround;
            BipedBody.RotateAngleY = MathHelper.Sin(MathHelper.Sqrt(swingProgress) * MathF.PI * 2.0f) * 0.2f;
            BipedRightArm.RotationPointZ = MathHelper.Sin(BipedBody.RotateAngleY) * 5.0f;
            BipedRightArm.RotationPointX = -MathHelper.Cos(BipedBody.RotateAngleY) * 5.0f;
            BipedLeftArm.RotationPointZ = -MathHelper.Sin(BipedBody.RotateAngleY) * 5.0f;
            BipedLeftArm.RotationPointX = MathHelper.Cos(BipedBody.RotateAngleY) * 5.0f;
            BipedRightArm.RotateAngleY += BipedBody.RotateAngleY;
            BipedLeftArm.RotateAngleY += BipedBody.RotateAngleY;
            BipedLeftArm.RotateAngleX += BipedBody.RotateAngleY;
            swingProgress = 1.0f - OnGround;
            swingProgress *= swingProgress;
            swingProgress *= swingProgress;
            swingProgress = 1.0f - swingProgress;
            float attackSwing = MathHelper.Sin(swingProgress * MathF.PI);
            float headOffset = MathHelper.Sin(OnGround * MathF.PI) * -(BipedHead.RotateAngleX - 0.7f) * (12.0f / 16.0f);
            BipedRightArm.RotateAngleX -= (float)(attackSwing * 1.2d + headOffset);
            BipedRightArm.RotateAngleY += BipedBody.RotateAngleY * 2.0f;
            BipedRightArm.RotateAngleZ = MathHelper.Sin(OnGround * MathF.PI) * -0.4f;
        }
        else
        {
            BipedBody.RotateAngleY = 0.0f;
            BipedRightArm.RotationPointX = -5.0f;
            BipedRightArm.RotationPointZ = 0.0f;
            BipedLeftArm.RotationPointX = 5.0f;
            BipedLeftArm.RotationPointZ = 0.0f;
        }

        if (IsSneak)
        {
            BipedBody.RotateAngleX = 0.5f;
            BipedRightLeg.RotationPointZ = 4.0f;
            BipedLeftLeg.RotationPointZ = 4.0f;
            BipedRightLeg.RotationPointY = 9.0f;
            BipedLeftLeg.RotationPointY = 9.0f;
            BipedHead.RotationPointY = 1.0f;
        }
        else
        {
            BipedBody.RotateAngleX = 0.0f;
            BipedRightLeg.RotationPointZ = 0.0f;
            BipedLeftLeg.RotationPointZ = 0.0f;
            BipedRightLeg.RotationPointY = 12.0f;
            BipedLeftLeg.RotationPointY = 12.0f;
            BipedHead.RotationPointY = 0.0f;
        }

        BipedRightArm.RotateAngleZ += MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedLeftArm.RotateAngleZ -= MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedRightArm.RotateAngleX += MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
        BipedLeftArm.RotateAngleX -= MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
    }
}
