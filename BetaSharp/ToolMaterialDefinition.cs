using OmniBlock.Registries.Data;

namespace OmniBlock;

public sealed class ToolMaterialDefinition : DataAsset
{
    public int MaxUses { get; init; }
    public float Efficiency { get; init; }
    public int DamageBonus { get; init; }
    public int HarvestLevel { get; init; }
}
