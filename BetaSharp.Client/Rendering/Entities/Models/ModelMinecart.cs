namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelMinecart : BbModelEntityModel
{
    private readonly ModelPart _floor;

    public ModelMinecart() : base("minecart")
    {
        GetPart("bottom").RotateAngleX = MathF.PI * 0.5f;
        _floor = GetPart("floor");
        _floor.RotateAngleX = MathF.PI * -0.5f;
        GetPart("left").RotateAngleY = MathF.PI * 3f / 2f;
        GetPart("right").RotateAngleY = MathF.PI * 0.5f;
        GetPart("back").RotateAngleY = MathF.PI;
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float animationProgress, float netHeadYaw, float headPitch, float scale)
    {
        _floor.RotationPointY = 4f - animationProgress;
    }
}
