namespace BetaSharp.Client.Rendering.Entities.Bbmodel;

public class BbmodelEntityModel : Models.ModelBase
{
    private readonly IReadOnlyList<string> _renderOrder;
    private readonly IReadOnlyDictionary<string, Models.ModelPart> _parts;

    protected BbmodelEntityModel(BbmodelBuiltModel built)
    {
        _renderOrder = built.RenderOrder;
        _parts = built.Parts;
    }

    protected BbmodelEntityModel(string entityId, float inflationOffset = 0f)
        : this(BbmodelModelBuilder.Build(BbmodelLoader.LoadCached(entityId), inflationOffset))
    {
    }

    protected Models.ModelPart GetPart(string name)
    {
        if (!_parts.TryGetValue(name, out Models.ModelPart? part))
        {
            throw new KeyNotFoundException($"Bbmodel bone '{name}' was not found.");
        }

        return part;
    }

    protected bool TryGetPart(string name, out Models.ModelPart part) => _parts.TryGetValue(name, out part!);

    public override void render(
        float limbSwing,
        float limbSwingAmount,
        float ageInTicks,
        float netHeadYaw,
        float headPitch,
        float scale)
    {
        setRotationAngles(limbSwing, limbSwingAmount, ageInTicks, netHeadYaw, headPitch, scale);
        foreach (string boneName in _renderOrder)
        {
            _parts[boneName].render(scale);
        }
    }
}
