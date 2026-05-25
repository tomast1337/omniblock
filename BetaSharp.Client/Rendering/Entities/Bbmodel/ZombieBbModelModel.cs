using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public class ZombieBbModelModel : BipedBbModelModel
{
    public ZombieBbModelModel() : this("zombie")
    {
    }

    protected ZombieBbModelModel(string entityId) : base(entityId)
    {
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        base.setRotationAngles(limbSwing, limbSwingAmount, ageInTicks, netHeadYaw, headPitch, scale);

        float swingProgress = MathHelper.Sin(onGround * MathF.PI);
        float attackSwing = MathHelper.Sin((1.0f - (1.0f - onGround) * (1.0f - onGround)) * MathF.PI);

        BipedRightArm.rotateAngleZ = 0.0f;
        BipedLeftArm.rotateAngleZ = 0.0f;
        BipedRightArm.rotateAngleY = -(0.1f - swingProgress * 0.6f);
        BipedLeftArm.rotateAngleY = 0.1f - swingProgress * 0.6f;
        BipedRightArm.rotateAngleX = MathF.PI * -0.5f;
        BipedLeftArm.rotateAngleX = MathF.PI * -0.5f;
        BipedRightArm.rotateAngleX -= swingProgress * 1.2f - attackSwing * 0.4f;
        BipedLeftArm.rotateAngleX -= swingProgress * 1.2f - attackSwing * 0.4f;
        BipedRightArm.rotateAngleZ += MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedLeftArm.rotateAngleZ -= MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedRightArm.rotateAngleX += MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
        BipedLeftArm.rotateAngleX -= MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
    }
}
