namespace BetaSharp.Client.Rendering.Entities.Models;

public sealed class ModelSquid : BbModelEntityModel
{
    private readonly ModelPart[] _tentacles = new ModelPart[8];

    public ModelSquid() : base("squid")
    {
        for (int i = 0; i < _tentacles.Length; i++)
        {
            _tentacles[i] = GetPart($"tentacle{i}");
            double angle = i * Math.PI * -2.0 / 8 + Math.PI * 0.5;
            _tentacles[i].RotateAngleY = (float)angle;
        }
    }

    public override void SetRotationAngles(float limbSwing, float limbSwingAmount, float tentaclePitch, float netHeadYaw, float headPitch, float scale)
    {
        foreach (var t in _tentacles) t.RotateAngleX = tentaclePitch;
    }
}
