using System.Text.Json;
using System.Text.Json.Serialization;
using OmniBlock.Processes;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests.Recipes;

public sealed class ProcessDefinitionTests
{
    [Fact]
    public void Envelope_preserves_custom_provider_schema_without_knowing_its_fields()
    {
        var envelope = DeserializeEnvelope("""
                                           {
                                             "id":"example:crushed_iron",
                                             "type":"example:crusher",
                                             "input":{"item":"omniblock:iron_ore","count":1},
                                             "output":{"item":"example:iron_dust","count":2},
                                             "energy":4000,
                                             "chance":0.75
                                           }
                                           """);

        var providerDefinition = envelope.GetProviderDefinition()
            .Deserialize<CrusherDefinition>()!;

        Assert.Equal("example:crushed_iron", envelope.GetProcessId().ToString());
        Assert.Equal("example:crusher", envelope.GetProviderType().ToString());
        Assert.Equal("omniblock:iron_ore", providerDefinition.Input.Item);
        Assert.Equal(2, providerDefinition.Output.Count);
        Assert.Equal(4000, providerDefinition.Energy);
        Assert.Equal(0.75, providerDefinition.Chance);
        Assert.False(envelope.ProviderData.ContainsKey("type"));
        Assert.False(envelope.ProviderData.ContainsKey("id"));
    }

    [Theory]
    [InlineData("shaped", "omniblock:crafting_shaped")]
    [InlineData("shapeless", "omniblock:crafting_shapeless")]
    [InlineData("smelting", "omniblock:smelting")]
    [InlineData("example:chemical_reactor", "example:chemical_reactor")]
    public void Envelope_maps_legacy_types_at_the_boundary_only(string source, string expected)
    {
        var envelope = DeserializeEnvelope($$"""{"type":"{{source}}"}""");

        Assert.Equal(expected, envelope.GetProviderType().ToString());
    }

    [Fact]
    public void Asset_identity_is_used_when_json_omits_id()
    {
        var envelope = DeserializeEnvelope("""{"type":"omniblock:smelting"}""");
        envelope.Namespace = Namespace.Get("example");
        envelope.Name = "iron_dust";

        Assert.Equal("example:iron_dust", envelope.GetProcessId().ToString());
    }

    [Fact]
    public void Conflicting_declared_and_asset_ids_are_rejected()
    {
        var envelope = DeserializeEnvelope(
            """{"id":"other:dust","type":"example:crusher"}""");
        envelope.Namespace = Namespace.Get("example");
        envelope.Name = "dust";

        var error = Assert.Throws<InvalidOperationException>(envelope.GetProcessId);

        Assert.Contains("example:dust", error.Message);
        Assert.Contains("other:dust", error.Message);
    }

    [Fact]
    public void Built_in_schemas_contain_only_fields_owned_by_their_provider()
    {
        Assert.Equal(["Key", "Pattern", "Result"], PropertyNames<ShapedCraftingDefinition>());
        Assert.Equal(["Ingredients", "Result"], PropertyNames<ShapelessCraftingDefinition>());
        Assert.Equal(["Input", "Result"], PropertyNames<SmeltingDefinition>());
        Assert.DoesNotContain("Energy", PropertyNames<ShapedCraftingDefinition>());
        Assert.DoesNotContain("Pattern", PropertyNames<SmeltingDefinition>());
    }

    [Fact]
    public void Every_shipped_definition_deserializes_through_its_provider_owned_schema()
    {
        var loader = new DataAssetLoader<ProcessDefinition>("recipe", LoadLocations.Assets, false);
        loader.LoadFromPaths(null, null, null);
        Assert.False(loader.HasErrors, loader.FirstErrorMessage);
        IReadableRegistry<ProcessDefinition> catalog = loader;

        var counts = new Dictionary<ResourceLocation, int>();
        foreach (var key in catalog.Keys)
        {
            var envelope = catalog.GetOrThrow(key);
            var providerType = envelope.GetProviderType();
            var json = envelope.GetProviderDefinition();
            object schema = providerType == ProcessTypes.CraftingShaped
                ? json.Deserialize<ShapedCraftingDefinition>()!
                : providerType == ProcessTypes.CraftingShapeless
                    ? json.Deserialize<ShapelessCraftingDefinition>()!
                    : providerType == ProcessTypes.Smelting
                        ? json.Deserialize<SmeltingDefinition>()!
                        : throw new InvalidOperationException($"Unknown shipped provider '{providerType}'.");

            Assert.NotNull(schema);
            Assert.Equal(key, envelope.GetProcessId());
            counts[providerType] = counts.GetValueOrDefault(providerType) + 1;
        }

        Assert.Equal(119, counts[ProcessTypes.CraftingShaped]);
        Assert.Equal(31, counts[ProcessTypes.CraftingShapeless]);
        Assert.Equal(10, counts[ProcessTypes.Smelting]);
    }

    private static ProcessDefinition DeserializeEnvelope(string json) =>
        JsonSerializer.Deserialize<ProcessDefinition>(json)!;

    private static string[] PropertyNames<T>() =>
        [.. typeof(T).GetProperties().Select(property => property.Name).Order()];

    private sealed class CrusherDefinition
    {
        [JsonPropertyName("input")] public ProcessResourceStack Input { get; init; } = new();

        [JsonPropertyName("output")] public ProcessResourceStack Output { get; init; } = new();

        [JsonPropertyName("energy")] public int Energy { get; init; }

        [JsonPropertyName("chance")] public double Chance { get; init; }
    }

    private sealed class ProcessResourceStack
    {
        [JsonPropertyName("item")] public string Item { get; init; } = "";

        [JsonPropertyName("count")] public int Count { get; init; }
    }
}
