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
    private const string ExpectedSnapshot = "b92fbbacc472ab3c89d139659541a676653f8210";

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
        foreach (ItemDefinition definition in DefaultRegistries.Items)
        {
            Item item = Assert.IsAssignableFrom<Item>(Item.Items[definition.ProtocolId]);

            Assert.Equal(definition.ProtocolId, item.Id);
            Assert.Same(item, Item.ByName(definition.Name));
            Assert.Equal(definition.MaxStackSize, item.GetMaxCount());
            Assert.Equal(ExpectedDurability(definition), item.GetMaxDamage());
            Assert.Equal(definition.HasSubtypes, item.GetHasSubtypes());
            Assert.Equal($"item.{definition.TranslationKey ?? definition.Name}", item.GetItemName());

            if (!string.IsNullOrEmpty(definition.TextureId))
                Assert.Equal(Atlases.Items.IndexOf(definition.TextureId), item.GetTextureId(0));

            if (definition.CraftingReturnItemProtocolId is { } returnId)
            {
                Assert.True(item.HasContainerItem());
                Assert.Same(Item.Items[returnId], item.GetContainerItem());
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
        foreach (ItemDefinition definition in DefaultRegistries.Items)
        {
            switch (definition.Behavior)
            {
                case FoodBehaviorDefinition { ReturnItem: { } item }:
                    AssertItem(item, definition);
                    break;
                case ToolBehaviorDefinition tool:
                    Assert.NotNull(ToolMaterialRegistry.Get(tool.Material));
                    break;
                case SwordBehaviorDefinition sword:
                    Assert.NotNull(ToolMaterialRegistry.Get(sword.Material));
                    break;
                case HoeBehaviorDefinition hoe:
                    Assert.NotNull(ToolMaterialRegistry.Get(hoe.Material));
                    break;
                case ArmorBehaviorDefinition armor:
                    Assert.NotNull(ArmorMaterialRegistry.Get(armor.Material));
                    break;
                case FishingRodBehaviorDefinition rod:
                    Assert.True(Atlases.Items.IndexOf(rod.Cast) >= 0, $"Item '{definition.Name}' has unknown texture '{rod.Cast}'.");
                    break;
                case SeedsBehaviorDefinition seeds:
                    AssertBlock(seeds.PlacesBlock, definition);
                    break;
                case PlaceBlockBehaviorDefinition place:
                    AssertBlock(place.PlacesBlock, definition);
                    break;
                case DyeBehaviorDefinition dye:
                    foreach (string texture in dye.Textures)
                        Assert.True(Atlases.Items.IndexOf(texture) >= 0, $"Item '{definition.Name}' has unknown texture '{texture}'.");
                    break;
            }
        }

        foreach (BlockDefinition block in LoadBlocks())
        {
            foreach (LootEntryDefinition entry in block.LootTable?.Entries ?? [])
            {
                Assert.True(ItemLookup.TryGetItemId(entry.ItemName, out _),
                    $"Block '{block.Namespace}:{block.Name}' loot references unknown item '{entry.ItemName}'.");
            }
        }
    }

    private static string BuildSnapshot()
    {
        StringBuilder text = new();
        foreach (ItemDefinition definition in DefaultRegistries.Items.OrderBy(static item => item.ProtocolId))
        {
            Item item = Item.Items[definition.ProtocolId]!;
            text.Append(definition.ProtocolId.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(definition.Namespace).Append(':').Append(definition.Name)
                .Append(" definition=").Append(JsonSerializer.Serialize(definition, s_json))
                .Append(" runtime=").Append(item.GetType().Name)
                .Append(" behavior=").Append(definition.Behavior?.GetType().Name ?? "-")
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
            Item item = ContentRuntime.Current.BlockItems.Get(key);
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
        Assert.True(ItemLookup.TryGetItemId(key, out _), $"Item '{owner.Namespace}:{owner.Name}' references unknown item '{key}'.");

    private static void AssertBlock(string? key, ItemDefinition owner)
    {
        Assert.False(string.IsNullOrWhiteSpace(key), $"Item '{owner.Namespace}:{owner.Name}' has no referenced block.");
        ResourceLocation location = ResourceLocation.Parse(key!);
        bool resolved = ContentRuntime.Current.Blocks.TryGet(location, out _)
                        || (ItemLookup.TryGetItemId(location.Path, out int protocolId)
                            && ContentRuntime.Current.Blocks.TryGetByProtocolId(protocolId, out _))
                        || LoadBlocks().Any(block => block.Namespace == location.Namespace
                                                     && block.TranslationKey == location.Path);
        Assert.True(resolved,
            $"Item '{owner.Namespace}:{owner.Name}' references unknown block '{key}'.");
    }

    private static int ExpectedDurability(ItemDefinition definition) => definition.Behavior switch
    {
        ToolBehaviorDefinition tool => ToolMaterialRegistry.Get(tool.Material).MaxUses,
        SwordBehaviorDefinition sword => ToolMaterialRegistry.Get(sword.Material).MaxUses,
        HoeBehaviorDefinition hoe => ToolMaterialRegistry.Get(hoe.Material).MaxUses,
        ArmorBehaviorDefinition armor => (new[] { 11, 16, 15, 13 }[(int)armor.Slot] * 3) << ArmorMaterialRegistry.Get(armor.Material).ArmorLevel,
        _ => definition.MaxDurability
    };
}
