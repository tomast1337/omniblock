using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.BbModel;

public sealed class GhastBbModelModel : BbModelEntityModel
{
    private readonly ModelPart _body;
    private readonly ModelPart[] _tentacles = new ModelPart[9];

    public GhastBbModelModel() : base("ghast")
    {
        _body = GetPart("body");
        for (int i = 0; i < _tentacles.Length; i++)
        {
            _tentacles[i] = GetPart($"tentacle{i}");
        }
    }

    public override void setRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        for (int i = 0; i < _tentacles.Length; i++)
        {
            _tentacles[i].rotateAngleX = 0.2f * MathHelper.Sin(ageInTicks * 0.3f + i) + 0.4f;
        }
    }
}
