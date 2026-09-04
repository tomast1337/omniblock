using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;
using OmniBlock.Textures;

namespace OmniBlock.Tests.Catalog;

/// <summary>
/// Locks down the legacy item catalog before its construction is moved into ContentRuntimeBuilder.
/// The fingerprint covers the complete merged JSON definitions plus observable runtime composition.
/// </summary>
public sealed class ItemCatalogCharacterizationTests
{
    private const string ExpectedSnapshot = "81c5e6b41499e8e4de154624f6ff053a28f62b86";

    private static readonly JsonSerializerOptions s_json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void Shipped_item_catalog_matches_semantic_snapshot()
    {
        string catalog = BuildSnapshot();
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalog))).ToLowerInvariant()[..40];

        Assert.Equal(ExpectedSnapshot, fingerprint);
    }

    [Fact]
    public void Every_item_definition_matches_its_constructed_runtime_item()
    {
        foreach (ItemDefinition definition in TestItemCatalog.LoadDefinitions())
        {
            Item item = ContentRuntime.Current.Items.GetByProtocolId(definition.ProtocolId);

            Assert.Equal(definition.ProtocolId, item.Id);
            Assert.Same(item, ContentRuntime.Current.Items.Get(new ResourceLocation(definition.Namespace, definition.Name)));
            Assert.Equal(definition.MaxStackSize, item.GetMaxCount());
            Assert.Equal(ExpectedDurability(definition), item.GetMaxDamage());
            Assert.Equal(definition.HasSubtypes, item.GetHasSubtypes());
            Assert.Equal($"item.{definition.TranslationKey ?? definition.Name}", item.GetItemName());

            if (!string.IsNullOrEmpty(definition.TextureId))
                Assert.Equal(Atlases.Items.IndexOf(definition.TextureId), item.GetTextureId(0));

            if (definition.CraftingReturnItemProtocolId is { } returnId)
            {
                Assert.True(item.HasContainerItem());
                Assert.Same(ContentRuntime.Current.Items.GetByProtocolId(returnId), item.GetContainerItem());
            }
            else
            {
                Assert.False(item.HasContainerItem());
            }
        }
    }

    [Fact]
    public void Every_item_behavior_and_block_loot_reference_resolves()
    {
        foreach (ItemDefinition definition in TestItemCatalog.LoadDefinitions())
        {
            foreach (JsonElement behavior in definition.Behaviors)
            {
                string type = behavior.GetProperty("Type").GetString()!;
                switch (type)
                {
                    case "food" when behavior.TryGetProperty("ReturnItem", out JsonElement item) && item.ValueKind != JsonValueKind.Null:
                        AssertItem(item.GetString()!, definition);
                        break;
                    case "tool" or "sword" or "hoe":
                        Assert.NotNull(ToolMaterialRegistry.Get(behavior.GetProperty("Material").GetString()!));
                        break;
                    case "armor":
                        Assert.NotNull(ArmorMaterialRegistry.Get(behavior.GetProperty("Material").GetString()!));
                        break;
                    case "fishing_rod":
                        string cast = behavior.GetProperty("Cast").GetString()!;
                        Assert.True(Atlases.Items.IndexOf(cast) >= 0, $"Item '{definition.Name}' has unknown texture '{cast}'.");
                        break;
                    case "seeds" or "place_block":
                        AssertBlock(behavior.GetProperty("PlacesBlock").GetString(), definition);
                        break;
                    case "dye":
                        foreach (JsonElement texture in behavior.GetProperty("Textures").EnumerateArray())
                            Assert.True(Atlases.Items.IndexOf(texture.GetString()!) >= 0, $"Item '{definition.Name}' has unknown texture '{texture}'.");
                        break;
                }
            }
        }

        foreach (BlockDefinition block in LoadBlocks())
        {
            foreach (LootEntryDefinition entry in block.LootTable?.Entries ?? [])
            {
                Assert.True(ContentRuntime.Current.Items.TryParse(entry.ItemName, out _),
                    $"Block '{block.Namespace}:{block.Name}' loot references unknown item '{entry.ItemName}'.");
            }
        }
    }

    private static string BuildSnapshot()
    {
        StringBuilder text = new();
        foreach (ItemDefinition definition in TestItemCatalog.LoadDefinitions().OrderBy(static item => item.ProtocolId))
        {
            Item item = ContentRuntime.Current.Items.GetByProtocolId(definition.ProtocolId);
            text.Append(definition.ProtocolId.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(definition.Namespace).Append(':').Append(definition.Name)
                .Append(" definition=").Append(JsonSerializer.Serialize(definition, s_json))
                .Append(" runtime=").Append(item.GetType().Name)
                .Append(" behaviors=").Append(string.Join(',', definition.Behaviors.Select(static behavior => behavior.GetProperty("Type").GetString())))
                .Append(" runtimeBehavior=").Append(item.GetBehavior<IItemBehavior>()?.GetType().Name ?? "-")
                .Append(" translation=").Append(item.GetItemName())
                .Append(" stack=").Append(item.GetMaxCount().ToString(CultureInfo.InvariantCulture))
                .Append(" durability=").Append(item.GetMaxDamage().ToString(CultureInfo.InvariantCulture))
                .Append(" subtypes=").Append(item.GetHasSubtypes())
                .Append(" handheld=").Append(item.IsHandheld())
                .Append(" rod=").Append(item.IsHandheldRod())
                .Append(" texture0=").Append(item.GetTextureId(0).ToString(CultureInfo.InvariantCulture))
                .Append(" texture1=").Append(item.GetTextureId(1).ToString(CultureInfo.InvariantCulture))
                .Append(" texture15=").Append(item.GetTextureId(15).ToString(CultureInfo.InvariantCulture))
                .Append(" aliases=").Append(string.Join(',', item.GetItemAlias.Order(StringComparer.Ordinal)))
                .Append(" return=").Append(item.HasContainerItem() ? item.GetContainerItem().Id : -1)
                .AppendLine();
        }

        AppendMaterials<ToolMaterialDefinition>(text, RegistryDefinitions.ToolMaterials);
        AppendMaterials<ArmorMaterialDefinition>(text, RegistryDefinitions.ArmorMaterials);

        foreach (BlockDefinition block in LoadBlocks().OrderBy(static block => block.ProtocolId))
        {
            ResourceLocation key = new(block.Namespace, block.Name);
            Item item = ContentRuntime.Current.Items.GetByProtocolId(block.ProtocolId);
            text.Append("block-item ").Append(key).Append(" id=").Append(item.Id)
                .Append(" declared=").Append(block.BlockItem.Type)
                .Append(" runtime=").Append(item.GetType().Name)
                .Append(" translation=").Append(item.GetItemName())
                .AppendLine();
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendMaterials<T>(StringBuilder text, RegistryDefinition<T> registry) where T : DataAsset
    {
        var loader = new DataAssetLoader<T>(registry.AssetPath, LoadLocations.Assets, allowUnhandled: false);
        loader.LoadFromPaths(null, null, null);
        Assert.False(loader.HasErrors, loader.FirstErrorMessage);
        foreach (T definition in loader.OrderBy(static value => value.Name, StringComparer.Ordinal))
            text.Append(typeof(T).Name).Append(' ').Append(definition.Namespace).Append(':').Append(definition.Name)
                .Append(' ').Append(JsonSerializer.Serialize(definition, s_json)).AppendLine();
    }

    private static BlockDefinitionJsonLoader LoadBlocks()
    {
        var loader = new BlockDefinitionJsonLoader(RegistryDefinitions.Blocks.AssetPath, LoadLocations.Assets);
        loader.LoadFromPaths(null, null, null);
        Assert.False(loader.HasErrors, loader.FirstErrorMessage);
        return loader;
    }

    private static void AssertItem(string key, ItemDefinition owner) =>
        Assert.True(ContentRuntime.Current.Items.TryParse(key, out _), $"Item '{owner.Namespace}:{owner.Name}' references unknown item '{key}'.");

    private static void AssertBlock(string? key, ItemDefinition owner)
    {
        Assert.False(string.IsNullOrWhiteSpace(key), $"Item '{owner.Namespace}:{owner.Name}' has no referenced block.");
        ResourceLocation location = ResourceLocation.Parse(key!);
        bool resolved = ContentRuntime.Current.Blocks.TryGet(location, out _)
                        || (ContentRuntime.Current.Items.TryParse(location.Path, out ItemStack? stack)
                            && ContentRuntime.Current.Blocks.TryGetByProtocolId(stack.ItemId, out _))
                        || LoadBlocks().Any(block => block.Namespace == location.Namespace
                                                     && block.TranslationKey == location.Path);
        Assert.True(resolved,
            $"Item '{owner.Namespace}:{owner.Name}' references unknown block '{key}'.");
    }

    private static int ExpectedDurability(ItemDefinition definition)
    {
        if (definition.Behaviors.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } behavior)
            return definition.MaxDurability;
        string type = behavior.GetProperty("Type").GetString()!;
        return type switch
        {
            "tool" or "sword" or "hoe" => ToolMaterialRegistry.Get(behavior.GetProperty("Material").GetString()!).MaxUses,
            "armor" => (new[] { 11, 16, 15, 13 }[behavior.GetProperty("Slot").GetInt32()] * 3) << ArmorMaterialRegistry.Get(behavior.GetProperty("Material").GetString()!).ArmorLevel,
            _ => definition.MaxDurability
        };
    }
}
