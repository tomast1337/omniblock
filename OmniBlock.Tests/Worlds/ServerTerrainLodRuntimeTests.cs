using OmniBlock.NBT;
using OmniBlock.Server.Worlds;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class ServerTerrainLodRuntimeTests
{
    [Fact]
    public void Terrain_revision_advances_only_for_changed_blocks_or_metadata_and_round_trips()
    {
        var world = new FakeWorldContext();
        var chunk = Chunk(world, 2, -3);
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;

        chunk[0, 4, 0] = stone;
        Assert.Equal(1, chunk.TerrainRevision);
        chunk[0, 4, 0] = stone;
        Assert.Equal(1, chunk.TerrainRevision);
        chunk.SetBlockMeta(0, 4, 0, 3);
        Assert.Equal(2, chunk.TerrainRevision);
        chunk.SetBlockMeta(0, 4, 0, 3);
        chunk.SetLight(LightType.Block, 0, 4, 0, 7);
        Assert.Equal(2, chunk.TerrainRevision);

        NBTTagCompound level = new();
        RegionChunkStorage.storeChunkInCompound(chunk, world, level);
        var loaded = new Chunk(world, level);
        var serialized = TerrainLodSourceSnapshot.FromRegionNbt(level);

        Assert.Equal(2, loaded.TerrainRevision);
        Assert.Equal(2, serialized.TerrainRevision);
        Assert.Equal(2, TerrainLodSourceSnapshot.Capture(chunk).TerrainRevision);
    }

    [Fact]
    public async Task Runtime_persists_initial_and_edited_snapshots_and_reuses_cache_after_restart()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var chunk = Chunk(world, 4, 5);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            chunk[0, 0, 0] = stone;

            using (var runtime = new ServerTerrainLodRuntime(
                       0, materials, root, world, conversionCapacity: 2))
            {
                runtime.TrackChunk(chunk);
                runtime.Tick();
                await WaitUntil(() => runtime.Snapshot().Writer.Written == 1);

                chunk.SetBlockMeta(0, 0, 0, 2);
                runtime.Tick();
                Assert.Equal(1, runtime.Snapshot().DirtyChunks);
                runtime.Tick();
                await WaitUntil(() => runtime.Snapshot().Writer.Written == 2);
                Assert.Equal(0, runtime.Snapshot().DirtyChunks);
            }

            using var reopened = new ServerTerrainLodRuntime(
                0, materials, root, world, conversionCapacity: 2);
            reopened.TrackChunk(chunk);
            reopened.Tick();
            await WaitUntil(() => reopened.Snapshot().Writer.CacheHitsAcknowledged == 1);

            Assert.Equal(0, reopened.Snapshot().Writer.Written);
            Assert.Equal(1, reopened.Snapshot().Cache.ReadHits);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Continuous_edits_cannot_postpone_conversion_beyond_the_maximum_dirty_age()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var chunk = Chunk(world, 0, 0);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            chunk[0, 0, 0] = stone;
            using var runtime = new ServerTerrainLodRuntime(
                0, materials, root, world, conversionCapacity: 2);
            runtime.TrackChunk(chunk);
            runtime.Tick();
            await WaitUntil(() => runtime.Snapshot().Writer.Written == 1);

            for (var tick = 0; tick < 21; tick++)
            {
                chunk.SetBlockMeta(0, 0, 0, tick & 1);
                runtime.Tick();
            }
            await WaitUntil(() => runtime.Snapshot().Writer.Written >= 2);

            Assert.True(runtime.Snapshot().TerrainChangesObserved >= 20);
            Assert.True(runtime.Snapshot().SnapshotsSubmitted +
                runtime.Snapshot().SnapshotSubmissionsCoalesced >= 2);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Durably_committed_offline_snapshot_enters_the_same_cache_pipeline()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var chunk = Chunk(world, -8, 12);
            chunk[1, 2, 3] = world.Content.Blocks.Get("omniblock:stone").Id;
            var offline = InactiveChunkSnapshot.Capture(chunk, world);
            using var runtime = new ServerTerrainLodRuntime(
                0, materials, root, world, conversionCapacity: 2);

            runtime.SubmitOffline(offline);
            await WaitUntil(() => runtime.Snapshot().Writer.Written == 1);

            Assert.Equal(1, runtime.Snapshot().OfflineSnapshotsSubmitted);
            Assert.Equal(0, runtime.Snapshot().OfflineSnapshotsDropped);
            Assert.Equal(chunk.TerrainRevision,
                offline.CaptureTerrain().TerrainRevision);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static Chunk Chunk(FakeWorldContext world, int x, int z) =>
        new(world, new byte[ChuckFormat.ChunkSize], x, z);

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var directory = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-server-terrain-lod-{Guid.NewGuid():N}"));
        directory.Create();
        return directory;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(2);
        }
    }
}
