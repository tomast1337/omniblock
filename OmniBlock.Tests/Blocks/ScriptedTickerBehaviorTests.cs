using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Blocks;

public sealed class ScriptedTickerBehaviorTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x, int y, int z, int meta, int blockId) =>
        new(world, x, y, z, meta, blockId);

    [Fact]
    public void OnTick_WithRegisteredHook_InvokesHookWithPrimitiveArgs()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(1, 64, 2, TestBlocks.Get("stone").Id, 3);

        (TickHost Host, int X, int Y, int Z, int Meta, int BlockId)? captured = null;
        ScriptTickHookRegistry.Register("test:capture_hook",
            (host, x, y, z, meta, blockId) => captured = (host, x, y, z, meta, blockId));

        ScriptedTickerBehavior behavior = new("test:capture_hook");
        behavior.OnTick(TestBlocks.Get("stone"),
            Tick(world, 1, 64, 2, 3, TestBlocks.Get("stone").Id));

        Assert.NotNull(captured);
        Assert.Equal((1, 64, 2, 3, TestBlocks.Get("stone").Id), (captured!.Value.X, captured.Value.Y, captured.Value.Z, captured.Value.Meta, captured.Value.BlockId));
    }

    [Fact]
    public void TickHost_ReadsAndWritesThroughToTheWorld()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(5, 64, 5, TestBlocks.Get("stone").Id);

        ScriptTickHookRegistry.Register("test:mutate_hook", (host, x, y, z, _, _) =>
        {
            Assert.Equal(TestBlocks.Get("stone").Id, host.GetBlockId(x, y, z));
            host.SetBlock(x, y, z, TestBlocks.Get("dirt").Id);
            host.SetBlockMeta(x, y, z, 7);
        });

        ScriptedTickerBehavior behavior = new("test:mutate_hook");
        behavior.OnTick(TestBlocks.Get("stone"),
            Tick(world, 5, 64, 5, 0, TestBlocks.Get("stone").Id));

        Assert.Equal(TestBlocks.Get("dirt").Id, world.Reader.GetBlockId(5, 64, 5));
        Assert.Equal(7, world.Reader.GetBlockMeta(5, 64, 5));
    }

    [Fact]
    public void OnTick_WithUnregisteredHookKey_DoesNothing()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("stone").Id);

        ScriptedTickerBehavior behavior = new("test:never_registered_hook_key");

        behavior.OnTick(TestBlocks.Get("stone"),
            Tick(world, 0, 64, 0, 0, TestBlocks.Get("stone").Id));

        Assert.Equal(TestBlocks.Get("stone").Id, world.Reader.GetBlockId(0, 64, 0));
    }
}
