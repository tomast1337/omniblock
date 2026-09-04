using System.Text.Json;
using OmniBlock.Registries.Data;

namespace OmniBlock.Items;

public sealed class ItemDefinition : DataAsset
{
    public int ProtocolId { get; set; } = -1;
    public string? TranslationKey { get; init; }
    public int MaxStackSize { get; init; } = 64;
    public int MaxDurability { get; init; } = 0;
    public string TextureId { get; init; } = "";
    public bool Handheld { get; init; }
    public bool HandheldRod { get; init; }
    public bool HasSubtypes { get; init; }
    public int? CraftingReturnItemProtocolId { get; init; }
    public string[] RepairIngredients { get; init; } = [];

    /// <summary>Ordered behavior definitions built by namespaced providers.</summary>
    public JsonElement[] Behaviors { get; init; } = [];
}
