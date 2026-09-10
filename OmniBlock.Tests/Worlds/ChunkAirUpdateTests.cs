namespace OmniBlock.Tests.Worlds;

public sealed class ChunkAirUpdateTests
{
    [Fact]
    public void Network_style_update_can_clear_stale_metadata_on_air()
    {
        LightTestWorld world = new();
        var chunk = world.Chunks.Add(0, 0);
        chunk.Meta.SetNibble(3, 40, 5, 7);

        var changed = chunk.SetBlock(3, 40, 5, 0, 0);

        Assert.True(changed);
        Assert.Equal(0, chunk.GetBlockMeta(3, 40, 5));
        Assert.Equal(0, chunk.GetBlockId(3, 40, 5));
    }

    [Fact]
    public void Block_can_be_replaced_with_air()
    {
        LightTestWorld world = new();
        var chunk = world.Chunks.Add(0, 0);
        var stone = TestBlocks.Get("stone").Id;
        Assert.True(chunk.SetBlock(3, 40, 5, stone, 0));

        Assert.True(chunk.SetBlock(3, 40, 5, 0, 0));
        Assert.Equal(0, chunk.GetBlockId(3, 40, 5));
    }
}
