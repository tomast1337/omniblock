namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelSign : BbModelEntityModel
{
    public readonly ModelPart SignBoard;
    public readonly ModelPart SignStick;

    public ModelSign() : base("sign")
    {
        SignBoard = GetPart("signBoard");
        SignStick = GetPart("signStick");
    }

    public void Render()
    {
        SignBoard.Render(1.0F / 16.0F);
        SignStick.Render(1.0F / 16.0F);
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
    }
}
