using System.Diagnostics.CodeAnalysis;

namespace BetaSharp.Client.Rendering.Entities.Models;

public class BbModelEntityModel : ModelBase
{
    private readonly IReadOnlyDictionary<string, ModelPart> _parts;
    private readonly IReadOnlyList<string> _renderOrder;

    protected BbModelEntityModel(BbModelBuiltModel built)
    {
        _renderOrder = built.RenderOrder;
        _parts = built.Parts;
    }

    protected BbModelEntityModel(string entityId, float inflationOffset = 0f)
        : this(BbModelModelBuilder.Build(BbModelLoader.LoadCached(entityId), inflationOffset))
    {
    }

    protected ModelPart GetPart(string name) => !_parts.TryGetValue(name, out ModelPart? part) ? throw new KeyNotFoundException($"Bbmodel bone '{name}' was not found.") : part;

    protected bool TryGetPart(string name, [NotNullWhen(true)] out ModelPart? part) => _parts.TryGetValue(name, out part);

    public override void Render(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        SetRotationAngles(limbSwing, limbSwingAmount, ageInTicks, netHeadYaw, headPitch, scale);
        foreach (string boneName in _renderOrder)
        {
            _parts[boneName].Render(scale);
        }
    }
}
