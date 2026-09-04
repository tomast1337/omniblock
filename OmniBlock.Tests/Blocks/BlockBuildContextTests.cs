using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using Xunit.Sdk;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockBuildContextTests
{
    [Fact]
    public void Block_factory_resolves_every_external_dependency_through_context()
    {
        var material = MaterialRegistry.Get("stone");
        BlockSoundGroup defaultSound = new("default", 1, 1);
        BlockSoundGroup selectedSound = new("selected", 1, 1);
        List<string> calls = [];
        BehaviorBuildContext behaviors = new(
            _ => throw new XunitException("Unexpected block resolution."),
            _ => throw new XunitException("Unexpected item resolution."),
            key =>
            {
                calls.Add($"material:{key}");
                return material;
            },
            key =>
            {
                calls.Add($"texture:{key}");
                return key.Length;
            });
        BlockBuildContext context = new(
            behaviors,
            key =>
            {
                calls.Add($"sound:{key}");
                return key.Path == "powder" ? defaultSound : selectedSound;
            },
            key =>
            {
                calls.Add($"loot:{key}");
                return 42;
            },
            key =>
            {
                calls.Add($"block_entity:{key}");
                return static () => new GenericBlockEntity();
            });
        BlockDefinition definition = new()
        {
            Name = "injected",
            ProtocolId = 240,
            Material = "example:material",
            TextureId = "example/base",
            SoundGroup = "example:sound",
            FaceTextures = new Dictionary<string, string>
            {
                ["North"] = "example/north"
            },
            LootTable = new LootTableDefinition([new LootEntryDefinition("example:drop")]),
            TileEntity = "example:block_entity"
        };

        var block = BlockFactory.Create(definition, context);
        BlockFactory.AttachBehaviors(
            block, definition, new BlockBehaviorProviderRegistry(behaviors), context);

        Assert.Same(material, block.Material);
        Assert.Same(selectedSound, block.SoundGroup);
        Assert.Equal(42, block.GetDroppedItemId(0));
        Assert.IsType<GenericBlockEntity>(block.GetBlockEntity());
        Assert.Equal(
            [
                "material:example:material",
                "texture:example/base",
                "sound:omniblock:powder",
                "sound:example:sound",
                "texture:example/north",
                "loot:example:drop",
                "block_entity:example:block_entity"
            ],
            calls);
    }

    [Fact]
    public void Default_context_fails_clearly()
    {
        BlockBuildContext context = default;

        var error = Assert.Throws<InvalidOperationException>(() => context.ResolveSoundGroup("omniblock:powder"));

        Assert.Contains(nameof(BlockBuildContext), error.Message);
    }
}
