using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelGhast : BbModelEntityModel
{
    private readonly ModelPart _body;
    private readonly ModelPart[] _tentacles = new ModelPart[9];

    public ModelGhast() : base("ghast")
    {
        _body = GetPart("body");
        for (int i = 0; i < _tentacles.Length; i++)
        {
            _tentacles[i] = GetPart($"tentacle{i}");
        }
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        for (int i = 0; i < _tentacles.Length; i++)
        {
            _tentacles[i].RotateAngleX = 0.2f * MathHelper.Sin(ageInTicks * 0.3f + i) + 0.4f;
        }
    }
}
