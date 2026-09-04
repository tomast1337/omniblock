using System.Reflection;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;

namespace OmniBlock.Tests.Catalog;

public sealed class ItemRuntimeAtomicityTests
{
    [Fact]
    public void Failed_item_construction_leaves_the_published_runtime_unchanged()
    {
        ContentRuntime published = ContentRuntime.Current;
        ResourceLocation missing = ResourceLocation.Parse("example:missing_container");
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(ItemDefinition("broken", 31780,
            """{"Type":"food","HealAmount":1,"ReturnItem":"example:missing_container"}"""));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("omniblock:broken", error.Message);
        Assert.Contains(missing.ToString(), error.Message);
        Assert.Same(published, ContentRuntime.Current);
        Assert.False(published.Items.TryGetByProtocolId(31780, out _));
    }

    [Fact]
    public void Two_builders_create_independent_item_instances()
    {
        ContentRuntimeBuilder firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntimeBuilder secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        firstBuilder.AddItemDefinition(ItemDefinition("isolated", 31781));
        secondBuilder.AddItemDefinition(ItemDefinition("isolated", 31781));

        ContentRuntime first = firstBuilder.Build();
        ContentRuntime second = secondBuilder.Build();

        Assert.NotSame(first.Items, second.Items);
        Assert.NotSame(first.Items.Get("omniblock:isolated"), second.Items.Get("omniblock:isolated"));
    }

    [Fact]
    public void Items_are_immutable_after_runtime_publication()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(ItemDefinition("frozen", 31782));
        Item item = builder.Build().Items.Get("omniblock:frozen");

        Assert.True(item.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => item.SetMaxCount(2));
        Assert.Throws<InvalidOperationException>(() => item.SetMaxDamage(4));
        Assert.Throws<InvalidOperationException>(() => item.SetTextureId(7));
        Assert.Throws<InvalidOperationException>(() => item.SetItemName("changed"));
        Assert.Throws<InvalidOperationException>(() => item.SetAliases(["changed"]));
        Assert.Throws<InvalidOperationException>(() => item.AddBehavior(new TestBehavior()));
    }

    [Fact]
    public void Block_items_and_item_behaviors_reference_objects_from_the_same_runtime()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        BlockDefinition blockDefinition = new() { Name = "runtime_block", ProtocolId = 240 };
        Block block = new(240, 0, MaterialRegistry.Get("stone"), SoundGroupRegistry.Get("stone"));
        builder.AddBlock(blockDefinition, block);
        builder.BuildBlockItems([blockDefinition]);
        builder.AddItemDefinition(ItemDefinition("placer", 31783,
            """{"Type":"place_block","PlacesBlock":"omniblock:runtime_block"}"""));
        builder.AddItemDefinition(ItemDefinition("container", 31784));
        builder.AddItemDefinition(ItemDefinition("food", 31785,
            """{"Type":"food","HealAmount":1,"ReturnItem":"omniblock:container"}"""));

        ContentRuntime runtime = builder.Build();
        ItemBlock blockItem = Assert.IsType<ItemBlock>(runtime.Items.GetByProtocolId(240));
        PlaceBlockBehavior placement = Assert.IsType<PlaceBlockBehavior>(
            runtime.Items.Get("omniblock:placer").GetBehavior<IItemBehavior>());
        FoodBehavior food = Assert.IsType<FoodBehavior>(
            runtime.Items.Get("omniblock:food").GetBehavior<IItemBehavior>());

        Assert.Same(runtime.Blocks.Get("omniblock:runtime_block"), blockItem.RuntimeBlock);
        Assert.Same(runtime.Blocks.Get("omniblock:runtime_block"), placement.PlacedBlock);
        Assert.Same(runtime.Items.Get("omniblock:container"), food.ReturnItem);
    }

    [Fact]
    public void Failed_drafts_cannot_escape_through_legacy_static_item_storage()
    {
        FieldInfo[] legacyArrays = typeof(Item).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static field => field.FieldType == typeof(Item[]) ||
                                   typeof(IDictionary<string, Item>).IsAssignableFrom(field.FieldType))
            .ToArray();

        Assert.Empty(legacyArrays);
        Assert.Null(typeof(Item).Assembly.GetType("OmniBlock.ItemLookup"));
        Assert.Null(typeof(DefaultRegistries).GetField("Items",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
    }

    private static ItemDefinition ItemDefinition(string name, int protocolId, string? behavior = null) => new()
    {
        Name = name,
        ProtocolId = protocolId,
        Behaviors = behavior is null ? [] : [JsonSerializer.Deserialize<JsonElement>(behavior)]
    };

    private sealed class TestBehavior : IItemBehavior;
}
