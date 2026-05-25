using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class ChickenBbModelModel : BbModelEntityModel
{
    private readonly ModelPart _bill;
    private readonly ModelPart _body;
    private readonly ModelPart _chin;
    private readonly ModelPart _head;
    private readonly ModelPart _leftLeg;
    private readonly ModelPart _leftWing;
    private readonly ModelPart _rightLeg;
    private readonly ModelPart _rightWing;

    public ChickenBbModelModel() : base("chicken")
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

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float wingRotation, float netHeadYaw, float headPitch, float scale)
    {
        _head.rotateAngleX = -(headPitch / (180.0f / MathF.PI));
        _head.rotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _bill.rotateAngleX = _head.rotateAngleX;
        _bill.rotateAngleY = _head.rotateAngleY;
        _chin.rotateAngleX = _head.rotateAngleX;
        _chin.rotateAngleY = _head.rotateAngleY;
        _body.rotateAngleX = MathF.PI * 0.5f;
        _rightLeg.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        _leftLeg.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        _rightWing.rotateAngleZ = wingRotation;
        _leftWing.rotateAngleZ = -wingRotation;
    }
}
