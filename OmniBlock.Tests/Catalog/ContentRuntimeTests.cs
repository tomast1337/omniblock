using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Worlds;

namespace OmniBlock.Tests.Catalog;

public sealed class ContentRuntimeTests
{
    [Fact]
    public void World_context_owns_the_injected_content_runtime()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        var isolated = builder.Build();

        FakeWorldContext world = new(isolated);

        Assert.Same(isolated, world.Content);
        Assert.NotSame(ContentRuntime.Current, world.Content);
    }

    [Fact]
    public void Published_runtime_contains_the_finalized_block_catalog_and_provider_registry()
    {
        var runtime = ContentRuntime.Current;
        Assert.NotEqual(0, runtime.Blocks.Count);
        Assert.Same(TestBlocks.Get("stone"), runtime.Blocks.Get("omniblock:stone"));
        Assert.Same(TestBlocks.Get("stone"), runtime.Blocks.GetByProtocolId(1));
        Assert.NotNull(runtime.BlockBehaviorProviders);
    }

    [Fact]
    public void Published_runtime_has_one_unified_item_registry()
    {
        var runtime = ContentRuntime.Current;
        var coal = ContentRuntime.Current.Items.Get("omniblock:coal");
        var stone = TestBlocks.Get("stone");

        Assert.Same(coal, runtime.Items.Get("omniblock:coal"));
        Assert.Same(coal, runtime.Items.GetByProtocolId(coal.Id));
        Assert.Same(runtime.Items.Get("omniblock:stone"), runtime.Items.GetByProtocolId(stone.Id));
    }

    [Fact]
    public void Published_runtime_owns_the_immutable_world_type_catalog()
    {
        var worldTypes = ContentRuntime.Current.WorldTypes;

        Assert.Equal(["default", "flat", "sky"], worldTypes.All.Select(static type => type.Name));
        Assert.Same(worldTypes.Get("omniblock:default"), worldTypes.Get("DEFAULT"));
        Assert.Same(worldTypes.Get("omniblock:flat"), worldTypes.Get("Flat"));
        Assert.False(worldTypes.TryGet("example:missing", out _));
        Assert.Throws<KeyNotFoundException>(() => worldTypes.Get("example:missing"));
        Assert.DoesNotContain(typeof(WorldType).GetProperties(),
            static property => property.SetMethod?.IsPublic == true);
        Assert.DoesNotContain(typeof(WorldType).GetFields(),
            static field => field.IsPublic && !field.IsInitOnly);
    }

    [Fact]
    public void Standalone_item_key_wins_legacy_block_item_name_collision_but_both_ids_resolve()
    {
        var runtime = ContentRuntime.Current;
        var bed = ContentRuntime.Current.Items.Get("omniblock:bed");
        var bedBlockItem = runtime.Items.GetByProtocolId(runtime.Blocks.Get("omniblock:bed").Id);

        Assert.Same(bed, runtime.Items.Get("omniblock:bed"));
        Assert.Same(bed, runtime.Items.GetByProtocolId(bed.Id));
        Assert.Same(bedBlockItem, runtime.Items.GetByProtocolId(bedBlockItem.Id));
        Assert.NotSame(bed, bedBlockItem);
    }

    [Fact]
    public void Published_blocks_are_frozen_and_reject_definition_mutation()
    {
        var stone = ContentRuntime.Current.Blocks.Get("stone");

        Assert.True(stone.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => stone.SetHardness(99));
        Assert.Throws<InvalidOperationException>(() => stone.SetBoundingBox(0, 0, 0, 0.5F, 0.5F, 0.5F));
        Assert.DoesNotContain(typeof(Block).GetProperties(), static property => property.SetMethod?.IsPublic == true);
        Assert.DoesNotContain(typeof(Block).GetFields(), static field => field.IsPublic && !field.IsInitOnly);
    }

    [Fact]
    public void Block_registry_public_lookup_delegates_to_the_published_runtime()
    {
        var runtime = ContentRuntime.Current;

        Assert.Same(runtime.Blocks.Get("omniblock:stone"), TestBlocks.Get("stone"));
        Assert.Same(runtime.Blocks.Get("omniblock:stone"), TestBlocks.Get("omniblock:stone"));
        Assert.Same(runtime.Blocks.GetByProtocolId(1), TestBlocks.GetByProtocolId(1));
        Assert.Throws<KeyNotFoundException>(() => TestBlocks.Get("example:stone"));
        Assert.Throws<KeyNotFoundException>(() => TestBlocks.GetByProtocolId(256));
    }

    [Fact]
    public void Derived_metadata_is_owned_by_runtime_blocks_with_explicit_air_defaults()
    {
        var glowstone = ContentRuntime.Current.Blocks.Get("omniblock:glowstone");
        var glass = ContentRuntime.Current.Blocks.Get("omniblock:glass");
        var chest = ContentRuntime.Current.Blocks.Get("omniblock:chest");

        Assert.Equal(glowstone.LightEmission, TestBlocks.GetLightEmission(glowstone.Id));
        Assert.Equal(glass.Opacity, TestBlocks.GetOpacity(glass.Id));
        Assert.Equal(glass.IsOpaque, TestBlocks.IsOpaque(glass.Id));
        Assert.Equal(chest.HasBlockEntity, TestBlocks.HasBlockEntity(chest.Id));
        Assert.True(TestBlocks.AllowsVision(0));
        Assert.False(TestBlocks.IsOpaque(0));
        Assert.Equal(0, TestBlocks.GetOpacity(0));
        Assert.Equal(0, TestBlocks.GetLightEmission(0));
        Assert.False(TestBlocks.HasBlockEntity(0));
        Assert.False(TestBlocks.TicksRandomly(0));
        Assert.False(TestBlocks.IgnoresMetaUpdates(0));
    }

    [Fact]
    public void Failed_builder_validation_does_not_replace_the_published_runtime()
    {
        var published = ContentRuntime.Current;
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        var stone = TestBlocks.Get("stone");
        BlockDefinition duplicate = new()
        {
            Name = "duplicate",
            ProtocolId = stone.Id
        };
        builder.AddBlock(duplicate, stone);
        builder.AddBlock(duplicate, stone);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void Builder_can_only_finalize_once()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        var runtime = builder.Build();

        Assert.Equal(0, runtime.Blocks.Count);
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Builders_produce_isolated_runtime_block_registries()
    {
        var firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var stone = TestBlocks.Get("stone");
        firstBuilder.AddBlock(Definition("first_stone", stone.Id), stone);
        secondBuilder.AddBlock(Definition("second_stone", stone.Id), stone);

        var first = firstBuilder.Build();
        var second = secondBuilder.Build();

        Assert.True(first.Blocks.TryGet("omniblock:first_stone", out _));
        Assert.False(first.Blocks.TryGet("omniblock:second_stone", out _));
        Assert.True(second.Blocks.TryGet("omniblock:second_stone", out _));
        Assert.False(second.Blocks.TryGet("omniblock:first_stone", out _));
        Assert.NotSame(first.BlockBehaviorProviders, second.BlockBehaviorProviders);
    }

    [Fact]
    public void Builders_own_new_blocks_without_global_protocol_id_collisions()
    {
        var firstDefinition = Definition("first_builder_block", 240);
        var secondDefinition = Definition("second_builder_block", 240);
        var firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var firstBlock = BlockFactory.Create(firstDefinition, firstBuilder.BlockBuildContext);
        var secondBlock = BlockFactory.Create(secondDefinition, secondBuilder.BlockBuildContext);
        firstBuilder.AddBlock(firstDefinition, firstBlock);
        secondBuilder.AddBlock(secondDefinition, secondBlock);

        var first = firstBuilder.Build();
        var second = secondBuilder.Build();

        Assert.NotSame(firstBlock, secondBlock);
        Assert.Same(firstBlock, first.Blocks.GetByProtocolId(240));
        Assert.Same(secondBlock, second.Blocks.GetByProtocolId(240));
    }

    [Fact]
    public void Definition_order_does_not_change_the_finalized_catalog()
    {
        var stone = TestBlocks.Get("stone");
        var dirt = TestBlocks.Get("dirt");
        var forwardBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        forwardBuilder.AddBlock(Definition("stone", stone.Id), stone);
        forwardBuilder.AddBlock(Definition("dirt", dirt.Id), dirt);
        var reverseBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        reverseBuilder.AddBlock(Definition("dirt", dirt.Id), dirt);
        reverseBuilder.AddBlock(Definition("stone", stone.Id), stone);

        var forward = forwardBuilder.Build();
        var reverse = reverseBuilder.Build();

        Assert.Equal(
            forward.Blocks.Keys.OrderBy(static key => key).Select(static key => key.ToString()),
            reverse.Blocks.Keys.OrderBy(static key => key).Select(static key => key.ToString()));
        Assert.Same(forward.Blocks.GetByProtocolId(stone.Id), reverse.Blocks.GetByProtocolId(stone.Id));
        Assert.Same(forward.Blocks.GetByProtocolId(dirt.Id), reverse.Blocks.GetByProtocolId(dirt.Id));
    }

    [Fact]
    public void Lazy_behavior_references_are_resolved_during_construction()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        var stairs = JsonSerializer.Deserialize<JsonElement>(
            """{"base":"omniblock:missing"}""");
        var melt = JsonSerializer.Deserialize<JsonElement>(
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
