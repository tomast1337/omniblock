using BetaSharp.Registries.Data;

namespace BetaSharp;

public sealed class ArmorMaterialDefinition : DataAsset
{
    public int ArmorLevel { get; init; }
    public int RenderIndex { get; init; }
}
