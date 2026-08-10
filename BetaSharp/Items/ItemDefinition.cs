using OmniBlock.Items.Behaviors;
using OmniBlock.Registries.Data;

namespace OmniBlock.Items;

public sealed class ItemDefinition : DataAsset
{
    public required int ProtocolId { get; init; }
    public string? TranslationKey { get; init; }
    public int MaxStackSize { get; init; } = 64;
    public int MaxDurability { get; init; } = 0;
    public string TextureId { get; init; } = "";
    public bool Handheld { get; init; }
    public bool HandheldRod { get; init; }
    public bool HasSubtypes { get; init; }
    public int? CraftingReturnItemProtocolId { get; init; }
    public ItemBehaviorDefinition? Behavior { get; init; }
}
