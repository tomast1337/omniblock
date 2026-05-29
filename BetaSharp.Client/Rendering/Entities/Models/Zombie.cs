using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public class Zombie : ModelBiped
{
    public Zombie() : this("zombie")
    {
    }

    protected Zombie(string entityId) : base(entityId)
    {
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        base.SetRotationAngles(limbSwing, limbSwingAmount, ageInTicks, netHeadYaw, headPitch, scale);

        float swingProgress = MathHelper.Sin(OnGround * MathF.PI);
        float attackSwing = MathHelper.Sin((1.0f - (1.0f - OnGround) * (1.0f - OnGround)) * MathF.PI);

        BipedRightArm.RotateAngleZ = 0.0f;
        BipedLeftArm.RotateAngleZ = 0.0f;
        BipedRightArm.RotateAngleY = -(0.1f - swingProgress * 0.6f);
        BipedLeftArm.RotateAngleY = 0.1f - swingProgress * 0.6f;
        BipedRightArm.RotateAngleX = MathF.PI * -0.5f;
        BipedLeftArm.RotateAngleX = MathF.PI * -0.5f;
        BipedRightArm.RotateAngleX -= swingProgress * 1.2f - attackSwing * 0.4f;
        BipedLeftArm.RotateAngleX -= swingProgress * 1.2f - attackSwing * 0.4f;
        BipedRightArm.RotateAngleZ += MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedLeftArm.RotateAngleZ -= MathHelper.Cos(ageInTicks * 0.09f) * 0.05f + 0.05f;
        BipedRightArm.RotateAngleX += MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
        BipedLeftArm.RotateAngleX -= MathHelper.Sin(ageInTicks * 0.067f) * 0.05f;
    }
}
