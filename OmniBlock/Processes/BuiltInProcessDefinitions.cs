using System.Text.Json.Serialization;

namespace OmniBlock.Processes;

/// <summary>Schema owned by the <c>omniblock:crafting_shaped</c> provider.</summary>
public sealed class ShapedCraftingDefinition
{
    [JsonPropertyName("pattern")] public string[] Pattern { get; init; } = [];

    [JsonPropertyName("key")] public Dictionary<string, string> Key { get; init; } = [];

    [JsonPropertyName("result")] public ProcessItemStackDefinition Result { get; init; } = new();
}

/// <summary>Schema owned by the <c>omniblock:crafting_shapeless</c> provider.</summary>
public sealed class ShapelessCraftingDefinition
{
    [JsonPropertyName("ingredients")] public string[] Ingredients { get; init; } = [];

    [JsonPropertyName("result")] public ProcessItemStackDefinition Result { get; init; } = new();
}

/// <summary>Schema owned by the <c>omniblock:smelting</c> provider.</summary>
public sealed class SmeltingDefinition
{
    [JsonPropertyName("input")] public string Input { get; init; } = "";

    [JsonPropertyName("result")] public ProcessItemStackDefinition Result { get; init; } = new();
}

/// <summary>Built-in item-stack reference shared by the built-in process providers.</summary>
public sealed class ProcessItemStackDefinition
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";

    [JsonPropertyName("count")] public int Count { get; init; } = 1;
}
