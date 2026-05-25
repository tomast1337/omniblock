using BetaSharp.Client.Rendering.Entities.Models;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class MinecartBbModelModel : BbModelEntityModel
{
    private readonly ModelPart _floor;

    public MinecartBbModelModel() : base("minecart")
    {
        GetPart("bottom").rotateAngleX = MathF.PI * 0.5f;
        _floor = GetPart("floor");
        _floor.rotateAngleX = MathF.PI * -0.5f;
        GetPart("left").rotateAngleY = MathF.PI * 3f / 2f;
        GetPart("right").rotateAngleY = MathF.PI * 0.5f;
        GetPart("back").rotateAngleY = MathF.PI;
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float animationProgress, float netHeadYaw, float headPitch, float scale)
    {
        _floor.rotationPointY = 4f - animationProgress;
    }
}
