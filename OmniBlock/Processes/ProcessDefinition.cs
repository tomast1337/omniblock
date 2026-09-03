using System.Text.Json;
using System.Text.Json.Serialization;
using OmniBlock.Registries.Data;

namespace OmniBlock.Processes;

/// <summary>
/// Common process envelope. All fields other than identity and provider type remain opaque to the
/// engine and are deserialized by the selected provider into its own schema.
/// </summary>
public sealed class ProcessDefinition : DataAsset
{
    [JsonPropertyName("id")]
    public string? DeclaredId { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ProviderData { get; init; } = [];

    public ResourceLocation GetProcessId()
    {
        if (string.IsNullOrWhiteSpace(DeclaredId)) return new ResourceLocation(Namespace, Name);

        ResourceLocation declared = ResourceLocation.Parse(DeclaredId);
        if (Name.Length != 0 && declared != new ResourceLocation(Namespace, Name))
            throw new InvalidOperationException(
                $"Process asset '{new ResourceLocation(Namespace, Name)}' declares conflicting id '{declared}'.");
        return declared;
    }

    public ResourceLocation GetProviderType() => Type switch
    {
        "shaped" => ProcessTypes.CraftingShaped,
        "shapeless" => ProcessTypes.CraftingShapeless,
        "smelting" => ProcessTypes.Smelting,
        _ => ResourceLocation.Parse(Type)
    };

    /// <summary>Returns only the provider-owned portion of the definition.</summary>
    public JsonElement GetProviderDefinition() => JsonSerializer.SerializeToElement(ProviderData);
}
