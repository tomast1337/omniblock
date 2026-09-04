using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Tests.Worlds;

public sealed class WorldContentRuntimeTests
{
    [Fact]
    public void Random_tick_ignores_air_missing_from_the_runtime_catalog()
    {
        LightTestWorld world = new();
        var emptyChunk = world.Chunks.Add(0, 0, populateLight: false);

        var error = Record.Exception(() => world.RandomTickBlock(emptyChunk, 0, 64, 0, 0, 0));

        Assert.Null(error);
    }

    [Fact]
    public void Lighting_queue_deduplicates_repeated_single_cell_updates()
    {
        LightTestWorld world = new();
        var stoneId = world.Content.Blocks.Get("stone").Id;
        world.Chunks.Add(0, 0,
            chunk => chunk.Blocks[ChuckFormat.GetIndex(1, 1) + 64] = (byte)stoneId,
            false);

        for (var i = 0; i < 10_000; i++)
            world.Lighting.QueueLightUpdate(LightType.Block, 1, 64, 1, 1, 64, 1, false);

        Assert.Equal(1, world.Lighting.PendingUpdateCount);
    }
}
