using System.Text.Json;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Catalog;

public sealed class BuilderOwnedItemTests
{
    [Fact]
    public void Builder_constructs_all_item_drafts_before_resolving_forward_references()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(new ItemDefinition
        {
            Name = "filled",
            ProtocolId = 31800,
            MaxStackSize = 1,
            CraftingReturnItemProtocolId = 31801,
            Behaviors = [JsonSerializer.Deserialize<JsonElement>("""{"Type":"food","HealAmount":1,"ReturnItem":"example:empty"}""")]
        });
        builder.AddItemDefinition(new ItemDefinition
        {
            Namespace = Namespace.Get("example"),
            Name = "empty",
            ProtocolId = 31801
        });

        ContentRuntime runtime = builder.Build();
        Item filled = runtime.Items.Get("omniblock:filled");
        Item empty = runtime.Items.Get("example:empty");

        Assert.Same(empty, filled.GetContainerItem());
        Assert.True(filled.IsFrozen);
        Assert.True(empty.IsFrozen);
        Assert.Same(filled, runtime.Items.GetByProtocolId(31800));
        Assert.Same(empty, runtime.Items.GetByProtocolId(31801));
        Assert.Null(Item.Items[31800]);
        Assert.Null(Item.Items[31801]);
    }

    [Fact]
    public void Finalized_runtime_items_reject_definition_mutation()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(new ItemDefinition { Name = "immutable", ProtocolId = 31802 });
        Item item = builder.Build().Items.Get("omniblock:immutable");

        Assert.Throws<InvalidOperationException>(() => item.SetMaxCount(2));
        Assert.Throws<InvalidOperationException>(() => item.SetTextureId(3));
        Assert.Throws<InvalidOperationException>(() => item.SetItemName("changed"));
        Assert.DoesNotContain(typeof(Item).GetProperties(), static property => property.SetMethod?.IsPublic == true);
        Assert.DoesNotContain(typeof(Item).GetFields(), static field => field.IsPublic && !field.IsStatic && !field.IsInitOnly);
    }

    [Fact]
    public void Failed_cross_reference_validation_exposes_no_partial_items()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(new ItemDefinition
        {
            Name = "broken",
            ProtocolId = 31803,
            MaxStackSize = 1,
            CraftingReturnItemProtocolId = 31804
        });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("omniblock:broken", error.Message);
        Assert.Contains("31804", error.Message);
        Assert.Null(Item.Items[31803]);
    }

    [Fact]
    public void Independently_built_runtimes_do_not_share_item_instances()
    {
        ContentRuntimeBuilder first = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntimeBuilder second = ContentRuntimeBuilder.CreateBuiltIns();
        first.AddItemDefinition(new ItemDefinition { Name = "isolated", ProtocolId = 31805 });
        second.AddItemDefinition(new ItemDefinition { Name = "isolated", ProtocolId = 31805 });

        Item firstItem = first.Build().Items.Get("omniblock:isolated");
        Item secondItem = second.Build().Items.Get("omniblock:isolated");

        Assert.NotSame(firstItem, secondItem);
    }
}
