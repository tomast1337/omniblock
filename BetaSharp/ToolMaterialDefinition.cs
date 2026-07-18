using BetaSharp.Registries.Data;

namespace BetaSharp;

public sealed class ToolMaterialDefinition : DataAsset
{
    public int MaxUses { get; init; }
    public float Efficiency { get; init; }
    public int DamageBonus { get; init; }
    public int HarvestLevel { get; init; }
}
