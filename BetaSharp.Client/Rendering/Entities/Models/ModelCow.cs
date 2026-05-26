using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelCow : BbModelEntityModel
{
    private readonly ModelPart _body;
    private readonly ModelPart _head;
    private readonly ModelPart _horn1;
    private readonly ModelPart _horn2;
    private readonly ModelPart _leg1;
    private readonly ModelPart _leg2;
    private readonly ModelPart _leg3;
    private readonly ModelPart _leg4;
    private readonly ModelPart _udders;

    public ModelCow() : base("cow")
    {
        _head = GetPart("head");
        _body = GetPart("body");
        _leg1 = GetPart("leg1");
        _leg2 = GetPart("leg2");
        _leg3 = GetPart("leg3");
        _leg4 = GetPart("leg4");
        _horn1 = GetPart("horn1");
        _horn2 = GetPart("horn2");
        _udders = GetPart("udders");
        _udders.RotateAngleX = MathF.PI * 0.5f;
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        _head.RotateAngleX = headPitch / (180.0f / MathF.PI);
        _head.RotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _body.RotateAngleX = MathF.PI * 0.5f;
        _leg1.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        _leg2.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        _leg3.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        _leg4.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        _horn1.RotateAngleX = _head.RotateAngleX;
        _horn1.RotateAngleY = _head.RotateAngleY;
        _horn2.RotateAngleX = _head.RotateAngleX;
        _horn2.RotateAngleY = _head.RotateAngleY;
    }
}
