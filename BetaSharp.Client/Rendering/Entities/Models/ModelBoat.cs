namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelBoat : BbModelEntityModel
{
    public ModelBoat() : base("boat")
    {
        GetPart("bottom").RotateAngleX = MathF.PI * 0.5f;
        GetPart("left").RotateAngleY = MathF.PI * 3f / 2f;
        GetPart("right").RotateAngleY = MathF.PI * 0.5f;
        GetPart("back").RotateAngleY = MathF.PI;
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
    }
}
