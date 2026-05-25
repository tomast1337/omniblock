using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class SheepFurBbModelModel : BbModelEntityModel
{
    private readonly ModelPart _body;
    private readonly ModelPart _head;
    private readonly ModelPart _leg1;
    private readonly ModelPart _leg2;
    private readonly ModelPart _leg3;
    private readonly ModelPart _leg4;

    public SheepFurBbModelModel() : base("sheepfur")
    {
        _head = GetPart("head");
        _body = GetPart("body");
        _leg1 = GetPart("leg1");
        _leg2 = GetPart("leg2");
        _leg3 = GetPart("leg3");
        _leg4 = GetPart("leg4");
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        _head.rotateAngleX = headPitch / (180.0f / MathF.PI);
        _head.rotateAngleY = netHeadYaw / (180.0f / MathF.PI);
        _body.rotateAngleX = MathF.PI * 0.5f;
        _leg1.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
        _leg2.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        _leg3.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f + MathF.PI) * 1.4f * limbSwingAmount;
        _leg4.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662f) * 1.4f * limbSwingAmount;
    }
}
