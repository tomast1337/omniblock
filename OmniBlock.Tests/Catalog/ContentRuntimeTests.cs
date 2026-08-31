using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Catalog;

public sealed class ContentRuntimeTests
{
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
