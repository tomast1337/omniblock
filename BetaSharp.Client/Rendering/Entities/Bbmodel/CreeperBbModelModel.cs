using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class CreeperBbModelModel : BbModelEntityModel
{
    private readonly ModelPart _body;
    private readonly ModelPart _head;
    private readonly ModelPart _leg1;
    private readonly ModelPart _leg2;
    private readonly ModelPart _leg3;
    private readonly ModelPart _leg4;

    public CreeperBbModelModel()
        : base("creeper")
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
        _head.rotateAngleY = netHeadYaw / (180.0F / (float)Math.PI);
        _head.rotateAngleX = headPitch / (180.0F / (float)Math.PI);
        _leg1.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662F) * 1.4F * limbSwingAmount;
        _leg2.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662F + (float)Math.PI) * 1.4F * limbSwingAmount;
        _leg3.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662F + (float)Math.PI) * 1.4F * limbSwingAmount;
        _leg4.rotateAngleX = MathHelper.Cos(limbSwing * 0.6662F) * 1.4F * limbSwingAmount;
    }
}
