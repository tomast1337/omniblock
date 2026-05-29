using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelChicken : BbModelEntityModel
{
    private readonly ModelPart _bill;
    private readonly ModelPart _body;
    private readonly ModelPart _chin;
    private readonly ModelPart _head;
    private readonly ModelPart _leftLeg;
    private readonly ModelPart _leftWing;
    private readonly ModelPart _rightLeg;
    private readonly ModelPart _rightWing;

    public ModelChicken() : base("chicken")
    {
        _head = GetPart("head");
        _bill = GetPart("bill");
        _chin = GetPart("chin");
        _body = GetPart("body");
        _rightLeg = GetPart("rightLeg");
        _leftLeg = GetPart("leftLeg");
        _rightWing = GetPart("rightWing");
        _leftWing = GetPart("leftWing");
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float wingRotation, float netHeadYaw, float headPitch, float scale)
    {
        _head.RotateAngleX = -(headPitch / (180.0f / MathF.PI));
        _head.RotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _bill.RotateAngleX = _head.RotateAngleX;
        _bill.RotateAngleY = _head.RotateAngleY;
        _chin.RotateAngleX = _head.RotateAngleX;
        _chin.RotateAngleY = _head.RotateAngleY;
        _body.RotateAngleX = MathF.PI * 0.5f;
        _rightLeg.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        _leftLeg.RotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        _rightWing.RotateAngleZ = wingRotation;
        _leftWing.RotateAngleZ = -wingRotation;
    }
}
