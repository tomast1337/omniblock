using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodRefinementSourcesTests
{
    [Fact]
    public void Retained_fine_source_survives_unload_and_capture_disposal()
    {
        var world = new FakeWorldContext();
        var chunk = world.ChunkHost.GetChunk(0, 0);
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        chunk.Blocks[ChuckFormat.GetIndex(1, 70, 2)] = (byte)stone;
        using var retained = new TerrainLodRefinementSources(1024 * 1024, 192);
        using (var original = Snapshot(world, 0, 0))
        {
            var capture = new TerrainLodVisualCaptures.Capture(
                new TerrainLodSourceLifetime(chunk), original, Source(0, 0));
            retained.Retain((0, 0), capture, 8, 8);
        }
        chunk.Loaded = false;
        chunk.Blocks[ChuckFormat.GetIndex(1, 70, 2)] = 0;

        Assert.True(retained.TryGet((0, 0), out var fine));
        Assert.True(fine.Lifetime.IsCurrent(null));
        Assert.Equal(stone, fine.Visuals.GetBlockId(1, 70, 2));
        Assert.True(retained.RetainedBytes > 0);
    }

    [Fact]
    public void Budget_prefers_nearer_sources_and_prunes_moved_away_sources()
    {
        var world = new FakeWorldContext();
        using var near = Snapshot(world, 0, 0);
        using var far = Snapshot(world, 5, 0);
        var chunk = world.ChunkHost.GetChunk(0, 0);
        var nearCapture = new TerrainLodVisualCaptures.Capture(
            new TerrainLodSourceLifetime(chunk), near, Source(0, 0));
        var farCapture = new TerrainLodVisualCaptures.Capture(
            new TerrainLodSourceLifetime(chunk), far, Source(5, 0));
        var oneEntryBudget = near.RetainedArrayBytes + nearCapture.Source!.EstimatedBytes;
        using var retained = new TerrainLodRefinementSources(oneEntryBudget, 192);

        retained.Retain((0, 0), nearCapture, 8, 8);
        retained.Retain((5, 0), farCapture, 8, 8);
        Assert.True(retained.TryGet((0, 0), out _));
        Assert.False(retained.TryGet((5, 0), out _));
        Assert.InRange(retained.RetainedBytes, 1, oneEntryBudget);

        retained.Prune(1000, 1000);
        Assert.Equal(0, retained.Count);
        Assert.Equal(0, retained.RetainedBytes);
    }

    private static TerrainLodSourceSnapshot Source(int x, int z) =>
        new(x, z, 16, ChuckFormat.WorldHeight, 16,
            new byte[ChuckFormat.ChunkSize], new byte[ChuckFormat.ChunkSize]);

    private static WorldRegionSnapshot Snapshot(FakeWorldContext world, int x, int z) =>
        new(world, x * 16, 0, z * 16, x * 16 + 15, ChuckFormat.WorldHeight - 1, z * 16 + 15);
}
