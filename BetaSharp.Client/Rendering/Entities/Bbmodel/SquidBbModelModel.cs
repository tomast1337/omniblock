using BetaSharp.Client.Rendering.Entities.Models;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class SquidBbModelModel : BbModelEntityModel
{
    private readonly ModelPart[] _tentacles = new ModelPart[8];

    public SquidBbModelModel() : base("squid")
    {
        for (int i = 0; i < _tentacles.Length; i++)
        {
            _tentacles[i] = GetPart($"tentacle{i}");
            double angle = i * Math.PI * -2.0 / 8 + Math.PI * 0.5;
            _tentacles[i].rotateAngleY = (float)angle;
        }
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float tentaclePitch, float netHeadYaw, float headPitch, float scale)
    {
        for (int i = 0; i < _tentacles.Length; i++)
        {
            _tentacles[i].rotateAngleX = tentaclePitch;
        }
    }
}
