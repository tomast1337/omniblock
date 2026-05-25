namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class BoatBbModelModel : BbModelEntityModel
{
    public BoatBbModelModel() : base("boat")
    {
        GetPart("bottom").rotateAngleX = MathF.PI * 0.5f;
        GetPart("left").rotateAngleY = MathF.PI * 3f / 2f;
        GetPart("right").rotateAngleY = MathF.PI * 0.5f;
        GetPart("back").rotateAngleY = MathF.PI;
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
    }
}
