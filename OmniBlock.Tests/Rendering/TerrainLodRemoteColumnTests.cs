using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Network.Messages;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodRemoteColumnTests
{
    [Theory]
    [InlineData(2, 1, true, true)]
    [InlineData(2, 3, false, true)]
    [InlineData(2, 3, true, false)]
    [InlineData(0, 0, false, false)]
    public void Refreshed_column_yields_only_to_an_installed_newer_aggregate(
        long leaf, long aggregate, bool matchesMesh, bool expected) =>
        Assert.Equal(expected, TerrainLodSpatialAuthority.PreferRefreshedColumn(leaf, aggregate, matchesMesh));

    [Fact]
    public void Leaf_refresh_preserves_metadata_lighting_and_visuals_without_loading_chunks()
    {
        var world = new FakeWorldContext();
        var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], -2, 3);
        chunk.Loaded = true;
        var fence = world.Content.Blocks.Get("omniblock:fence").Id;
        chunk[15, 95, 7] = fence;
        chunk.SetBlockMeta(15, 95, 7, 3);
        chunk.SetLight(LightType.Sky, 15, 95, 7, 12);
        chunk.SetLight(LightType.Block, 15, 95, 7, 5);
        var original = TerrainLodSourceSnapshot.Capture(chunk);
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var tile = TerrainLodColumnTile.BuildLeaf(original, materials);
        var source = TerrainLodRemoteColumnSource.Expand(tile, world.Content.Blocks, true);
        // Poison the live source: refresh visuals must use only the immutable reply.
        chunk[15, 95, 7] = 0;
        using var visuals = new WorldRegionSnapshot(world, source);
        Assert.Equal(fence, visuals.GetBlockId(-17, 95, 55));
        Assert.Equal(3, source.GetMetadata(15, 95, 7));
        Assert.Equal(original.TerrainRevision, source.TerrainRevision);
        var light = source.Lighting!.GetLightLevels(-17, 95, 55, 0);
        Assert.Equal(12, light.Sky);
        Assert.Equal(5, light.Block);
        Assert.Equal(fence, source.GetBlock(15, 95, 7));
    }

    [Fact]
    public void Superseded_remote_work_and_new_gameplay_chunk_reject_old_remote_result()
    {
        var generation = 1;
        var lifetime = new TerrainLodSourceLifetime(17, () => generation == 1);
        Assert.True(lifetime.IsCurrent(null));
        generation = 2;
        Assert.False(lifetime.IsCurrent(null));
        generation = 1;
        var world = new FakeWorldContext();
        var reloaded = world.ChunkHost.GetChunk(0, 0);
        reloaded.Loaded = true;
        Assert.False(lifetime.IsCurrent(reloaded));
        var oldLocal = new TerrainLodSourceLifetime(reloaded);
        reloaded.Loaded = false;
        Assert.True(oldLocal.IsCurrent(null));
        Assert.False(oldLocal.IsCurrent(null, latestRemoteGeneration: 2));
        reloaded.Loaded = true;
        Assert.True(oldLocal.IsCurrent(reloaded, latestRemoteGeneration: 2));
    }

    [Fact]
    public void Leaf_and_aggregate_requests_round_trip_but_level_one_is_not_admitted()
    {
        TerrainLodTileRequestMessage message = new()
        {
            CacheIdentity = "test",
            Keys = [new(0, -2, 3), new(2, -1, 0)]
        };
        using MemoryStream wire = new();
        message.Write(wire);
        wire.Position = 0;
        TerrainLodTileRequestMessage read = new();
        read.Read(wire);
        Assert.Equal(message.Keys, read.Keys);
        Assert.Equal(6, read.SchemaVersion);
        message.Keys = [new(1, 0, 0)];
        Assert.Throws<InvalidOperationException>(() => message.Write(new MemoryStream()));
    }
}
