using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelCreeper : BbModelEntityModel
{
    private readonly ModelPart _body;
    private readonly ModelPart _head;
    private readonly ModelPart _leg1;
    private readonly ModelPart _leg2;
    private readonly ModelPart _leg3;
    private readonly ModelPart _leg4;

    public ModelCreeper(float inflationOffset = 0f)
        : base("creeper", inflationOffset)
    {
        _head = GetPart("head");
        _body = GetPart("body");
        _leg1 = GetPart("leg1");
        _leg2 = GetPart("leg2");
        _leg3 = GetPart("leg3");
        _leg4 = GetPart("leg4");
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        _head.RotateAngleY = netHeadYaw / (180.0F / (float)Math.PI);
        _head.RotateAngleX = headPitch / (180.0F / (float)Math.PI);
        _leg1.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662F) * 1.4F * limbSwingAmount;
        _leg2.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662F + (float)Math.PI) * 1.4F * limbSwingAmount;
        _leg3.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662F + (float)Math.PI) * 1.4F * limbSwingAmount;
        _leg4.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662F) * 1.4F * limbSwingAmount;
    }
}
