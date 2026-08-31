using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests.Catalog;

public sealed class CatalogConstructionTests
{
    [Fact]
    public void Every_shipped_block_definition_has_a_constructed_block_and_all_declared_slots()
    {
        BlockDefinitionJsonLoader loader = LoadBlocks();

        foreach (BlockDefinition definition in loader)
        {
            Block block = BlockRegistry.Get(definition.Name);
            Assert.Equal(definition.ProtocolId, block.Id);

            foreach (JsonElement behavior in definition.Behaviors)
            {
                foreach (JsonElement slot in behavior.GetProperty("Slots").EnumerateArray())
                {
                    AssertBlockSlotAttached(block, slot.GetString()!);
                }
            }
        }
    }

    [Fact]
    public void Every_shipped_item_definition_has_a_constructed_item()
    {
        foreach (ItemDefinition definition in DefaultRegistries.Items)
        {
            Item item = Assert.IsAssignableFrom<Item>(Item.Items[definition.ProtocolId]);
            Assert.Equal(definition.ProtocolId, item.Id);
            Assert.Same(item, Item.ByName(definition.Name));
        }
    }

    [Fact]
    public void Every_shipped_entity_definition_has_a_constructed_type_and_all_declared_slots()
    {
        foreach (EntityType type in DefaultRegistries.EntityTypes.Where(static type => type.Definition is not null))
        {
            EntityDefinition definition = type.RequireDefinition();
            Assert.Same(type, EntityRegistry.ByName(definition.Name));
            Assert.Equal(definition.ProtocolId, DefaultRegistries.EntityTypes.GetId(type));

            foreach (var behavior in definition.Behaviors)
            {
                foreach (string slot in behavior.Slots)
                {
                    AssertEntitySlotAttached(type, slot);
                }
            }
        }
    }

    [Fact]
    public void Unknown_block_behavior_type_fails_loudly()
    {
        JsonElement json = JsonSerializer.Deserialize<JsonElement>("""{"Type":"example:missing","Slots":["Ticker"]}""");

        ArgumentException error = Assert.Throws<ArgumentException>(() => BehaviorRegistry.Build("example:missing", json));

        Assert.Contains("example:missing", error.Message);
    }

    [Theory]
    [InlineData("block", -1)]
    [InlineData("item", 255)]
    [InlineData("entity", 128)]
    public void Invalid_protocol_id_fails_during_definition_loading(string catalog, int protocolId)
    {
        using TemporaryCatalog temporary = new(catalog, protocolId);
        DataAssetLoader loader = catalog switch
        {
            "block" => new BlockDefinitionJsonLoader("block", LoadLocations.Assets),
            "item" => new ItemDefinitionJsonLoader("item", LoadLocations.Assets),
            "entity" => new EntityDefinitionJsonLoader("entity", LoadLocations.Assets),
            _ => throw new ArgumentOutOfRangeException(nameof(catalog))
        };

        loader.LoadFromPaths(temporary.Root, null, null);

        Assert.True(loader.HasErrors);
        Assert.Contains("ProtocolId", loader.FirstErrorMessage);
    }

    private static BlockDefinitionJsonLoader LoadBlocks()
    {
        BlockDefinitionJsonLoader loader = new(RegistryDefinitions.Blocks.AssetPath, LoadLocations.Assets);
        loader.LoadFromPaths(null, null, null);
        Assert.False(loader.HasErrors, loader.FirstErrorMessage);
        return loader;
    }

    private static void AssertBlockSlotAttached(Block block, string slot)
    {
        object? attached = slot switch
        {
            "Ticker" => block.Ticker,
            "Physics" => block.Physics,
            "Lifecycle" => block.Lifecycle,
            "Visuals" => block.Visuals,
            "Interactable" => block.Interactable,
            "Redstone" => block.Redstone,
            _ => throw new Xunit.Sdk.XunitException($"Unknown block slot '{slot}'.")
        };

        Assert.True(attached is not null, $"Block {block.Id} declares slot '{slot}', but it was not attached.");
    }

    private static void AssertEntitySlotAttached(EntityType type, string slot)
    {
        object? attached = slot switch
        {
            "Ticker" => type.Behaviors.Ticker,
            "Attack" => type.Behaviors.Attack,
            "Targeting" => type.Behaviors.Targeting,
            "Loot" => type.Behaviors.Loot,
            "Interactable" => type.Behaviors.Interactable,
            "Physics" => type.Behaviors.Physics,
            "Persistence" => type.Behaviors.Persistence,
            "Lifecycle" => type.Behaviors.Lifecycle,
            _ => throw new Xunit.Sdk.XunitException($"Unknown entity slot '{slot}' on '{type.Id}'.")
        };

        Assert.True(attached is not null, $"Entity '{type.Id}' declares slot '{slot}', but it was not attached.");
    }

    private sealed class TemporaryCatalog : IDisposable
    {
        public TemporaryCatalog(string catalog, int protocolId)
        {
            Root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string directory = Path.Combine(Root, "assets", catalog);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "invalid.json"), $$"""{"ProtocolId":{{protocolId}}}""");
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
