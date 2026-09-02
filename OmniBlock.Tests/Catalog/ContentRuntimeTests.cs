using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Catalog;

public sealed class ContentRuntimeTests
{
    [Fact]
    public void World_context_owns_the_injected_content_runtime()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntime isolated = builder.Build();

        FakeWorldContext world = new(isolated);

        Assert.Same(isolated, world.Content);
        Assert.NotSame(ContentRuntime.Current, world.Content);
    }

    [Fact]
    public void Published_runtime_contains_the_finalized_block_catalog_and_provider_registry()
    {
        ContentRuntime runtime = ContentRuntime.Current;
        Assert.NotEqual(0, runtime.Blocks.Count);
        Assert.Same(BlockRegistry.Get("stone"), runtime.Blocks.Get("omniblock:stone"));
        Assert.Same(BlockRegistry.Get("stone"), runtime.Blocks.GetByProtocolId(1));
        Assert.NotNull(runtime.BlockBehaviorProviders);
    }

    [Fact]
    public void Published_runtime_has_one_unified_item_registry()
    {
        ContentRuntime runtime = ContentRuntime.Current;
        Item coal = Item.ByName("coal");
        Block stone = BlockRegistry.Get("stone");

        Assert.Same(coal, runtime.Items.Get("omniblock:coal"));
        Assert.Same(coal, runtime.Items.GetByProtocolId(coal.Id));
        Assert.Same(runtime.Items.Get("omniblock:stone"), runtime.Items.GetByProtocolId(stone.Id));
    }

    [Fact]
    public void Standalone_item_key_wins_legacy_block_item_name_collision_but_both_ids_resolve()
    {
        ContentRuntime runtime = ContentRuntime.Current;
        Item bed = Item.ByName("bed");
        Item bedBlockItem = runtime.Items.GetByProtocolId(runtime.Blocks.Get("omniblock:bed").Id);

        Assert.Same(bed, runtime.Items.Get("omniblock:bed"));
        Assert.Same(bed, runtime.Items.GetByProtocolId(bed.Id));
        Assert.Same(bedBlockItem, runtime.Items.GetByProtocolId(bedBlockItem.Id));
        Assert.NotSame(bed, bedBlockItem);
    }

    [Fact]
    public void Published_blocks_are_frozen_and_reject_definition_mutation()
    {
        Block stone = ContentRuntime.Current.Blocks.Get("stone");

        Assert.True(stone.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => stone.SetHardness(99));
        Assert.Throws<InvalidOperationException>(() => stone.SetBoundingBox(0, 0, 0, 0.5F, 0.5F, 0.5F));
        Assert.DoesNotContain(typeof(Block).GetProperties(), static property => property.SetMethod?.IsPublic == true);
        Assert.DoesNotContain(typeof(Block).GetFields(), static field => field.IsPublic && !field.IsInitOnly);
    }

    [Fact]
    public void Block_registry_public_lookup_delegates_to_the_published_runtime()
    {
        ContentRuntime runtime = ContentRuntime.Current;

        Assert.Same(runtime.Blocks.Get("omniblock:stone"), BlockRegistry.Get("stone"));
        Assert.Same(runtime.Blocks.Get("omniblock:stone"), BlockRegistry.Get("omniblock:stone"));
        Assert.Same(runtime.Blocks.GetByProtocolId(1), BlockRegistry.GetByProtocolId(1));
        Assert.Throws<KeyNotFoundException>(() => BlockRegistry.Get("example:stone"));
        Assert.Throws<KeyNotFoundException>(() => BlockRegistry.GetByProtocolId(256));
    }

    [Fact]
    public void Derived_metadata_is_owned_by_runtime_blocks_with_explicit_air_defaults()
    {
        Block glowstone = ContentRuntime.Current.Blocks.Get("omniblock:glowstone");
        Block glass = ContentRuntime.Current.Blocks.Get("omniblock:glass");
        Block chest = ContentRuntime.Current.Blocks.Get("omniblock:chest");

        Assert.Equal(glowstone.LightEmission, BlockRegistry.GetLightEmission(glowstone.Id));
        Assert.Equal(glass.Opacity, BlockRegistry.GetOpacity(glass.Id));
        Assert.Equal(glass.IsOpaque, BlockRegistry.IsOpaque(glass.Id));
        Assert.Equal(chest.HasBlockEntity, BlockRegistry.HasBlockEntity(chest.Id));
        Assert.True(BlockRegistry.AllowsVision(0));
        Assert.False(BlockRegistry.IsOpaque(0));
        Assert.Equal(0, BlockRegistry.GetOpacity(0));
        Assert.Equal(0, BlockRegistry.GetLightEmission(0));
        Assert.False(BlockRegistry.HasBlockEntity(0));
        Assert.False(BlockRegistry.TicksRandomly(0));
        Assert.False(BlockRegistry.IgnoresMetaUpdates(0));
    }

    [Fact]
    public void Failed_builder_validation_does_not_replace_the_published_runtime()
    {
        ContentRuntime published = ContentRuntime.Current;
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        Block stone = BlockRegistry.Get("stone");
        BlockDefinition duplicate = new() { Name = "duplicate", ProtocolId = stone.Id };
        builder.AddBlock(duplicate, stone);
        builder.AddBlock(duplicate, stone);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void Builder_can_only_finalize_once()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntime runtime = builder.Build();

        Assert.Equal(0, runtime.Blocks.Count);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Builders_produce_isolated_runtime_block_registries()
    {
        ContentRuntimeBuilder firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntimeBuilder secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        Block stone = BlockRegistry.Get("stone");
        firstBuilder.AddBlock(Definition("first_stone", stone.Id), stone);
        secondBuilder.AddBlock(Definition("second_stone", stone.Id), stone);

        ContentRuntime first = firstBuilder.Build();
        ContentRuntime second = secondBuilder.Build();

        Assert.True(first.Blocks.TryGet("omniblock:first_stone", out _));
        Assert.False(first.Blocks.TryGet("omniblock:second_stone", out _));
        Assert.True(second.Blocks.TryGet("omniblock:second_stone", out _));
        Assert.False(second.Blocks.TryGet("omniblock:first_stone", out _));
        Assert.NotSame(first.BlockBehaviorProviders, second.BlockBehaviorProviders);
    }

    [Fact]
    public void Builders_own_new_blocks_without_global_protocol_id_collisions()
    {
        BlockDefinition firstDefinition = Definition("first_builder_block", 240);
        BlockDefinition secondDefinition = Definition("second_builder_block", 240);
        ContentRuntimeBuilder firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntimeBuilder secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        Block firstBlock = BlockFactory.Create(firstDefinition, firstBuilder.BlockBuildContext);
        Block secondBlock = BlockFactory.Create(secondDefinition, secondBuilder.BlockBuildContext);
        firstBuilder.AddBlock(firstDefinition, firstBlock);
        secondBuilder.AddBlock(secondDefinition, secondBlock);

        ContentRuntime first = firstBuilder.Build();
        ContentRuntime second = secondBuilder.Build();

        Assert.NotSame(firstBlock, secondBlock);
        Assert.Same(firstBlock, first.Blocks.GetByProtocolId(240));
        Assert.Same(secondBlock, second.Blocks.GetByProtocolId(240));
    }

    [Fact]
    public void Definition_order_does_not_change_the_finalized_catalog()
    {
        Block stone = BlockRegistry.Get("stone");
        Block dirt = BlockRegistry.Get("dirt");
        ContentRuntimeBuilder forwardBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        forwardBuilder.AddBlock(Definition("stone", stone.Id), stone);
        forwardBuilder.AddBlock(Definition("dirt", dirt.Id), dirt);
        ContentRuntimeBuilder reverseBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        reverseBuilder.AddBlock(Definition("dirt", dirt.Id), dirt);
        reverseBuilder.AddBlock(Definition("stone", stone.Id), stone);

        ContentRuntime forward = forwardBuilder.Build();
        ContentRuntime reverse = reverseBuilder.Build();

        Assert.Equal(
            forward.Blocks.Keys.OrderBy(static key => key).Select(static key => key.ToString()),
            reverse.Blocks.Keys.OrderBy(static key => key).Select(static key => key.ToString()));
        Assert.Same(forward.Blocks.GetByProtocolId(stone.Id), reverse.Blocks.GetByProtocolId(stone.Id));
        Assert.Same(forward.Blocks.GetByProtocolId(dirt.Id), reverse.Blocks.GetByProtocolId(dirt.Id));
    }

    [Fact]
    public void Lazy_behavior_references_are_resolved_during_construction()
    {
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        JsonElement stairs = JsonSerializer.Deserialize<JsonElement>(
            """{"base":"omniblock:missing"}""");
        JsonElement melt = JsonSerializer.Deserialize<JsonElement>(
            """{"melt_replacement":"omniblock:missing"}""");

        Assert.Throws<KeyNotFoundException>(() => builder.BuildBlockBehavior("omniblock:stairs", stairs));
        Assert.Throws<KeyNotFoundException>(() => builder.BuildBlockBehavior("omniblock:melt", melt));
    }

    private static BlockDefinition Definition(string name, int protocolId) => new()
    {
        Name = name,
        ProtocolId = protocolId
    };
}
