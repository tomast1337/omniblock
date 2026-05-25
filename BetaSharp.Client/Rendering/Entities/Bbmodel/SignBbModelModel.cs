using BetaSharp.Client.Rendering.Entities.Models;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class SignBbModelModel : BbModelEntityModel
{
    public readonly ModelPart SignBoard;
    public readonly ModelPart SignStick;

    public SignBbModelModel() : base("sign")
    {
        SignBoard = GetPart("signBoard");
        SignStick = GetPart("signStick");
    }

    public void Render()
    {
        SignBoard.render(1.0F / 16.0F);
        SignStick.render(1.0F / 16.0F);
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
    }
}
