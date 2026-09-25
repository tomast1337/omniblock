using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.NBT;
using OmniBlock.Network.Messages;
using OmniBlock.Server.Worlds;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Storage.RegionFormat;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Worlds;

public sealed class ServerTerrainLodRuntimeTests
{
    [Fact]
    public void Preparation_diagnostics_include_every_build_and_persistence_stage()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world);
            var idle = runtime.Snapshot();
            Assert.Equal(0, idle.PreparationPendingWork);
            Assert.Equal(0, idle.PreparationFailureEvents);

            Assert.Equal(1, (idle with { DirtyChunks = 1 }).PreparationPendingWork);
            Assert.Equal(1, (idle with
            {
                Conversion = idle.Conversion with { OwnedChunks = 1 }
            }).PreparationPendingWork);
            var hierarchy = idle.SpatialHierarchy;
            Assert.NotNull(hierarchy.Persistence);
            foreach (var busy in new[]
                     {
                         hierarchy with { DeferredParents = 1 },
                         hierarchy with { Construction = hierarchy.Construction with { Owned = 1 } },
                         hierarchy with { Persistence = hierarchy.Persistence with { Queued = 1 } },
                         hierarchy with { Persistence = hierarchy.Persistence with { Running = 1 } }
                     })
                Assert.Equal(1, (idle with { SpatialHierarchy = busy }).PreparationPendingWork);

            // Failure and idleness are separate: a failed/oversize write cannot qualify as ready.
            var failed = idle with
            {
                SpatialHierarchy = hierarchy with
                {
                    Persistence = hierarchy.Persistence with { Failed = 1 }
                }
            };
            Assert.Equal(0, failed.PreparationPendingWork);
            Assert.Equal(1, failed.PreparationFailureEvents);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

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

            await runtime.SubmitOfflineAsync(offline, CancellationToken.None);
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

    [Fact]
    public async Task Offline_batch_larger_than_conversion_capacity_is_admitted_without_loss()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world, conversionCapacity: 1);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
            for (var x = 0; x < 8; x++)
            {
                var chunk = Chunk(world, x, 0);
                chunk[0, 4, 0] = world.Content.Blocks.Get("omniblock:stone").Id;
                await runtime.SubmitOfflineAsync(InactiveChunkSnapshot.Capture(chunk, world), timeout.Token);
                Assert.InRange(runtime.Snapshot().Conversion.OwnedChunks, 0, 1);
            }
            await WaitUntil(() => runtime.Snapshot().Writer.Written == 8);
            Assert.Equal(8, runtime.Snapshot().OfflineSnapshotsSubmitted);
            Assert.Equal(0, runtime.Snapshot().OfflineSnapshotsDropped);
            Assert.Equal(0, runtime.Snapshot().PreparationFailureEvents);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_builds_and_reopens_spatial_parent_coverage_without_loading_chunks()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            using (var runtime = new ServerTerrainLodRuntime(
                       0, materials, root, world, conversionCapacity: 8))
            {
                for (var x = 0; x < 2; x++)
                for (var z = 0; z < 2; z++)
                {
                    var chunk = Chunk(world, x, z);
                    chunk[0, 0, 0] = world.Content.Blocks.Get("omniblock:stone").Id;
                    runtime.SubmitOffline(InactiveChunkSnapshot.Capture(chunk, world));
                }

                var parentKey = new TerrainLodTileKey(1, 0, 0);
                await WaitUntil(() =>
                    runtime.TryGetSpatialCoverage(parentKey, out _) &&
                    runtime.Snapshot().SpatialCache.Writes >= 5);
                Assert.True(runtime.TryGetSpatialCoverage(parentKey, out var parent));
                Assert.Equal(parentKey, parent!.Key);
            }

            using var reopened = new ServerTerrainLodRuntime(
                0, materials, root, world, conversionCapacity: 2);
            var reopenedKey = new TerrainLodTileKey(1, 0, 0);
            Assert.False(reopened.TryGetSpatialCoverage(reopenedKey, out _));
            await WaitUntil(() => reopened.TryGetSpatialCoverage(reopenedKey, out _));
            Assert.True(reopened.TryGetSpatialCoverage(reopenedKey, out var cached));
            Assert.Equal(1, cached!.Key.Level);
            await WaitUntil(() => reopened.Snapshot().SpatialCache.ReadHits == 1);
            Assert.Equal(1, reopened.Snapshot().SpatialCache.ReadHits);
            Assert.Equal(0, reopened.Snapshot().TrackedChunks);
            Assert.Equal(0, reopened.Snapshot().SpatialWirePayloads);
            Assert.False(reopened.TryGetSpatialPayload(reopenedKey, out _));
            await WaitUntil(() => reopened.TryGetSpatialPayload(reopenedKey, out _));
            Assert.True(reopened.TryGetSpatialPayload(reopenedKey, out var payload));
            Assert.Equal(1, reopened.Snapshot().SpatialWirePayloads);
            var transported = TerrainLodTileMessage.FromCompressed(0, payload!).Decode();
            Assert.Equal(cached.CanonicalHash, transported.CanonicalHash);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Cold_absent_spatial_record_becomes_an_explicit_cache_miss()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            using var runtime = new ServerTerrainLodRuntime(
                0,
                TerrainLodMaterialCatalog.FromRuntime(world.Content),
                root,
                world,
                conversionCapacity: 2);
            var key = new TerrainLodTileKey(3, 9, -4);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Missing);
            Assert.Equal(TerrainLodTileAvailability.Missing,
                runtime.GetSpatialPayload(key, out _));
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Cold_saved_tile_imports_without_loading_gameplay_chunks_or_changing_region_bytes()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(save.FullName);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            for (var x = 0; x < 4; x++)
            for (var z = 0; z < 4; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                storage.SaveChunk(world, chunk, null, 1);
            }
            storage.FlushToDisk();
            var region = Path.Combine(save.FullName, "region", "r.0.0.mcr");
            var before = File.ReadAllBytes(region);
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                conversionCapacity: 4, storedTerrain: storage);
            var key = new TerrainLodTileKey(2, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Ready, TimeSpan.FromSeconds(20));

            Assert.True(runtime.TryGetSpatialCoverage(key, out var tile));
            Assert.Equal(key, tile!.Key);
            Assert.Equal(0, runtime.Snapshot().TrackedChunks);
            Assert.Equal(before, File.ReadAllBytes(region));
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Incomplete_saved_tile_stays_missing_without_generating_the_absent_chunk()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(save.FullName);
            var chunk = Chunk(world, 0, 0);
            chunk[0, 4, 0] = world.Content.Blocks.Get("omniblock:stone").Id;
            storage.SaveChunk(world, chunk, null, 1);
            storage.FlushToDisk();
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                storedTerrain: storage);
            var key = new TerrainLodTileKey(2, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Missing);

            Assert.False(storage.ContainsChunk(3, 3));
            Assert.Equal(0, runtime.Snapshot().TrackedChunks);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Cold_coarse_request_builds_transiently_without_resident_leaves()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(save.FullName);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            for (var x = 0; x < 8; x++)
            for (var z = 0; z < 8; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                storage.SaveChunk(world, chunk, null, 1);
            }
            storage.FlushToDisk();
            var region = Path.Combine(save.FullName, "region", "r.0.0.mcr");
            var before = File.ReadAllBytes(region);
            var coarse = new TerrainLodTileKey(3, 0, 0);
            string firstHash;
            using (var runtime = new ServerTerrainLodRuntime(
                       0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                       conversionCapacity: 8, storedTerrain: storage,
                       transientImportMinimumLevel: 3))
            {
                Assert.Equal(TerrainLodTileAvailability.Pending,
                    runtime.GetSpatialCoverage(coarse, out _));
                await WaitUntil(() => runtime.GetSpatialCoverage(coarse, out _) ==
                                      TerrainLodTileAvailability.Ready, TimeSpan.FromSeconds(30));

                Assert.True(runtime.TryGetSpatialCoverage(coarse, out var tile));
                Assert.Equal(coarse, tile!.Key);
                firstHash = tile.CanonicalHash;
                Assert.Equal(0, runtime.Snapshot().TrackedChunks);
                Assert.Equal(64, runtime.Snapshot().SavedChunksImported);
                Assert.Equal(0, runtime.Snapshot().SpatialHierarchy.Leaves);
                Assert.Equal(0, runtime.Snapshot().SpatialHierarchy.CurrentTiles);
            }
            using (var reopened = new ServerTerrainLodRuntime(
                       0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                       storedTerrain: storage, transientImportMinimumLevel: 3))
            {
                await WaitUntil(() => reopened.TryGetSpatialCoverage(coarse, out _));
                Assert.True(reopened.TryGetSpatialCoverage(coarse, out var cached));
                Assert.Equal(firstHash, cached!.CanonicalHash);
                Assert.Equal(0, reopened.Snapshot().SavedChunksImported);
            }
            Assert.Equal(before, File.ReadAllBytes(region));
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Cold_high_level_request_with_no_saved_source_reports_missing()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                storedTerrain: new RegionChunkStorage(save.FullName));
            var key = new TerrainLodTileKey(6, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Missing);

            Assert.Equal(0, runtime.Snapshot().SavedChunksImported);
            Assert.Equal(0, runtime.Snapshot().SavedTileImportsPending);
            Assert.Equal(1, runtime.Snapshot().SavedTileImportsMissing);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Dormant_horizon_levels_do_not_start_unbounded_saved_source_scans()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                spatialPolicy: TerrainLodSpatialPolicy.CreateForMaximumHorizon(512),
                storedTerrain: new RegionChunkStorage(root.FullName));
            var key = new TerrainLodTileKey(7, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Missing);

            Assert.Equal(0, runtime.Snapshot().SavedChunksImported);
            Assert.Equal(0, runtime.Snapshot().SavedTileImportsPending);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Incomplete_transient_coarse_import_publishes_no_partial_tiles()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(save.FullName);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            for (var x = 0; x < 8; x++)
            for (var z = 0; z < 8; z++)
            {
                if (x == 7 && z == 7) continue;
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                storage.SaveChunk(world, chunk, null, 1);
            }
            storage.FlushToDisk();
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                storedTerrain: storage, transientImportMinimumLevel: 3);
            var key = new TerrainLodTileKey(3, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Missing, TimeSpan.FromSeconds(30));

            Assert.Equal(0, runtime.Snapshot().SpatialHierarchy.Tiles);
            Assert.Equal(0, runtime.Snapshot().SpatialCache.Writes);
            Assert.Equal(63, runtime.Snapshot().SavedChunksImported);
            Assert.Equal(1, runtime.Snapshot().SavedTileImportsMissing);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Live_edit_during_transient_import_rejects_mixed_revision_parent()
    {
        var root = CreateTemporaryDirectory();
        using var releaseRead = new ManualResetEventSlim();
        using var blockedRead = new ManualResetEventSlim();
        try
        {
            var world = new FakeWorldContext();
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            var sources = new Dictionary<(int X, int Z), TerrainLodSourceSnapshot>();
            Chunk? live = null;
            Chunk? secondLive = null;
            for (var x = 0; x < 8; x++)
            for (var z = 0; z < 8; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                sources.Add((x, z), TerrainLodSourceSnapshot.Capture(chunk));
                if (x == 0 && z == 0) live = chunk;
                if (x == 1 && z == 0) secondLive = chunk;
            }
            var storage = new PausingTerrainSourceStorage(sources, blockedRead, releaseRead);
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                storedTerrain: storage, transientImportMinimumLevel: 3);
            runtime.TrackChunk(live!);
            runtime.TrackChunk(secondLive!);
            var key = new TerrainLodTileKey(3, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => blockedRead.IsSet, TimeSpan.FromSeconds(30));
            // The source already consumed at (0,0) is now stale while a later read is in flight.
            live![0, 4, 0] = 0;
            releaseRead.Set();
            await WaitUntil(() => runtime.Snapshot().SavedTileImportsFailed == 1);
            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));

            Assert.Equal(0, runtime.Snapshot().SpatialHierarchy.Tiles);
            Assert.Equal(0, runtime.Snapshot().SpatialCache.Writes);
            Assert.Equal(1, runtime.Snapshot().SavedTileImportsFailed);

            // A later durable save removes the retry barrier and the replacement uses the
            // updated source, rather than publishing the rejected mixed-revision candidate.
            secondLive![0, 4, 0] = 0;
            storage.Replace(TerrainLodSourceSnapshot.Capture(live));
            runtime.NotifyChunkSaved(live);
            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            storage.Replace(TerrainLodSourceSnapshot.Capture(secondLive));
            runtime.NotifyChunkSaved(secondLive);
            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Ready, TimeSpan.FromSeconds(30));
            Assert.Equal(1, runtime.Snapshot().SpatialCache.Writes);
            Assert.Equal(1, runtime.Snapshot().SpatialHierarchy.Tiles);
            var expected = new TerrainLodTransientTileBuilder(
                key, TerrainLodSpatialPolicy.CreateDefault(),
                TerrainLodMaterialCatalog.FromRuntime(world.Content));
            while (expected.Result is null)
            {
                var (x, z) = expected.NextChunkCoordinates();
                expected.AddSource(sources[(x, z)]);
            }
            Assert.True(runtime.TryGetSpatialCoverage(key, out var replacement));
            Assert.Equal(expected.Result.CanonicalHash, replacement!.CanonicalHash);
        }
        finally
        {
            releaseRead.Set();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Live_edit_during_level_two_import_discards_old_reads_and_retries_after_save()
    {
        var root = CreateTemporaryDirectory();
        using var releaseRead = new ManualResetEventSlim();
        using var blockedRead = new ManualResetEventSlim();
        try
        {
            var world = new FakeWorldContext();
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            var sources = new Dictionary<(int X, int Z), TerrainLodSourceSnapshot>();
            Chunk? live = null;
            for (var x = 0; x < 4; x++)
            for (var z = 0; z < 4; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                sources.Add((x, z), TerrainLodSourceSnapshot.Capture(chunk));
                if (x == 0 && z == 0) live = chunk;
            }
            var storage = new PausingTerrainSourceStorage(sources, blockedRead, releaseRead);
            using var runtime = new ServerTerrainLodRuntime(
                0, materials, root, world, storedTerrain: storage);
            runtime.TrackChunk(live!);
            var key = new TerrainLodTileKey(2, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => blockedRead.IsSet, TimeSpan.FromSeconds(30));
            // The importer already consumed (0,0), then stops during a later saved-source read.
            live![0, 4, 0] = 0;
            releaseRead.Set();
            await WaitUntil(() => runtime.Snapshot().SavedTileImportsFailed == 1,
                TimeSpan.FromSeconds(30));
            Assert.False(runtime.TryGetSpatialCoverage(key, out _));
            Assert.InRange(runtime.Snapshot().SpatialHierarchy.Tiles, 0, 7);

            runtime.BeforeChunkSave(live);
            storage.Replace(TerrainLodSourceSnapshot.Capture(live));
            runtime.NotifyChunkSaved(live);
            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Ready,
                TimeSpan.FromSeconds(30));

            var expected = new TerrainLodTransientTileBuilder(
                key, TerrainLodSpatialPolicy.CreateDefault(), materials);
            while (expected.Result is null)
            {
                var (x, z) = expected.NextChunkCoordinates();
                expected.AddSource(sources[(x, z)]);
            }
            Assert.True(runtime.TryGetSpatialCoverage(key, out var replacement));
            Assert.Equal(expected.Result.CanonicalHash, replacement!.CanonicalHash);
        }
        finally
        {
            releaseRead.Set();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Edit_after_temporary_coarse_write_cannot_publish_stale_record_after_save()
    {
        var root = CreateTemporaryDirectory();
        using var staged = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        using var releaseRead = new ManualResetEventSlim(true);
        using var unusedReadPause = new ManualResetEventSlim();
        try
        {
            var world = new FakeWorldContext();
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            var sources = new Dictionary<(int X, int Z), TerrainLodSourceSnapshot>();
            Chunk? live = null;
            for (var x = 0; x < 8; x++)
            for (var z = 0; z < 8; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                sources.Add((x, z), TerrainLodSourceSnapshot.Capture(chunk));
                if (x == 0 && z == 0) live = chunk;
            }
            var storage = new PausingTerrainSourceStorage(
                sources, unusedReadPause, releaseRead);
            using var runtime = new ServerTerrainLodRuntime(
                0, materials, root, world, storedTerrain: storage,
                transientImportMinimumLevel: 3,
                spatialWriteObserver: stage =>
                {
                    if (stage != TerrainLodCacheWriteStage.TemporaryDurable) return;
                    staged.Set();
                    if (!releaseWrite.Wait(TimeSpan.FromSeconds(30)))
                        throw new TimeoutException("Test did not release the staged tile.");
                });
            runtime.TrackChunk(live!);
            var key = new TerrainLodTileKey(3, 0, 0);

            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => staged.IsSet, TimeSpan.FromSeconds(30));
            Assert.Empty(root.EnumerateFiles("*.ocol", SearchOption.AllDirectories));
            Assert.Single(root.EnumerateFiles("*.tmp-*", SearchOption.AllDirectories));

            live![0, 4, 0] = 0;
            runtime.BeforeChunkSave(live);
            storage.Replace(TerrainLodSourceSnapshot.Capture(live));
            runtime.NotifyChunkSaved(live);
            // A process dying at this exact point would reopen only the authoritative new
            // chunk; the old derived candidate is still an unpublished temporary file.
            var reopenedCache = new TerrainLodColumnTileCacheStore(root, runtime.Identity);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Missing,
                reopenedCache.Read(key).Status);

            releaseWrite.Set();
            await WaitUntil(() => runtime.Snapshot().SavedTileImportsFailed == 1,
                TimeSpan.FromSeconds(30));
            Assert.Empty(root.EnumerateFiles("*.ocol", SearchOption.AllDirectories));
            Assert.Equal(TerrainLodTileAvailability.Pending,
                runtime.GetSpatialCoverage(key, out _));
            await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                  TerrainLodTileAvailability.Ready,
                TimeSpan.FromSeconds(30));

            var expected = new TerrainLodTransientTileBuilder(
                key, TerrainLodSpatialPolicy.CreateDefault(), materials);
            while (expected.Result is null)
            {
                var (x, z) = expected.NextChunkCoordinates();
                expected.AddSource(sources[(x, z)]);
            }
            Assert.True(runtime.TryGetSpatialCoverage(key, out var replacement));
            Assert.Equal(expected.Result.CanonicalHash, replacement!.CanonicalHash);
        }
        finally
        {
            releaseWrite.Set();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Published_coarse_tile_is_replaced_after_save_and_reopen_uses_new_record()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(save.FullName);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            Chunk? edited = null;
            for (var x = 0; x < 8; x++)
            for (var z = 0; z < 8; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                storage.SaveChunk(world, chunk, null, 1);
                if (x == 0 && z == 0) edited = chunk;
            }
            storage.FlushToDisk();
            var key = new TerrainLodTileKey(3, 0, 0);
            string replacementHash;
            using (var runtime = new ServerTerrainLodRuntime(
                       0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                       storedTerrain: storage, transientImportMinimumLevel: 3))
            {
                await WaitUntil(() => runtime.TryGetSpatialCoverage(key, out _),
                    TimeSpan.FromSeconds(30));
                Assert.True(runtime.TryGetSpatialCoverage(key, out var original));
                runtime.TrackChunk(edited!);
                edited![0, 4, 0] = 0;

                Assert.Equal(TerrainLodTileAvailability.Pending,
                    runtime.GetSpatialCoverage(key, out _));
                runtime.BeforeChunkSave(edited);
                Assert.Equal(TerrainLodColumnTileCacheReadStatus.Missing,
                    new TerrainLodColumnTileCacheStore(root, runtime.Identity)
                        .Read(key).Status);
                storage.SaveChunk(world, edited, null, 2);
                storage.FlushToDisk();
                var firstInvalidation = Assert.Single(runtime.NotifyChunkSaved(edited));
                Assert.Equal(key, firstInvalidation.Key);
                edited[1, 4, 0] = stone;
                runtime.BeforeChunkSave(edited);
                storage.SaveChunk(world, edited, null, 3);
                storage.FlushToDisk();
                var secondInvalidation = Assert.Single(runtime.NotifyChunkSaved(edited));
                Assert.Equal(key, secondInvalidation.Key);
                Assert.True(secondInvalidation.Generation > firstInvalidation.Generation);
                await WaitUntil(() => runtime.GetSpatialCoverage(key, out _) ==
                                      TerrainLodTileAvailability.Ready,
                    TimeSpan.FromSeconds(30));
                Assert.True(runtime.TryGetSpatialCoverage(key, out var replacement));
                replacementHash = replacement!.CanonicalHash;
                Assert.NotEqual(original!.CanonicalHash, replacementHash);
            }

            using var reopened = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                storedTerrain: storage, transientImportMinimumLevel: 3);
            await WaitUntil(() => reopened.TryGetSpatialCoverage(key, out _));
            Assert.True(reopened.TryGetSpatialCoverage(key, out var cached));
            Assert.Equal(replacementHash, cached!.CanonicalHash);
            Assert.Equal(0, reopened.Snapshot().SavedChunksImported);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_edit_refreshes_normal_hierarchy_tile_and_reopen_rejects_old_record()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(save.FullName);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            Chunk? edited = null;
            for (var x = 0; x < 4; x++)
            for (var z = 0; z < 4; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                storage.SaveChunk(world, chunk, null, 1);
                if (x == 0 && z == 0) edited = chunk;
            }
            storage.FlushToDisk();
            var key = new TerrainLodTileKey(2, 0, 0);
            string replacementHash;
            using (var runtime = new ServerTerrainLodRuntime(
                       0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                       storedTerrain: storage))
            {
                await WaitUntil(() => runtime.TryGetSpatialCoverage(key, out _),
                    TimeSpan.FromSeconds(30));
                Assert.True(runtime.TryGetSpatialCoverage(key, out var original));
                await WaitUntil(() => new TerrainLodColumnTileCacheStore(root, runtime.Identity)
                    .Read(key).Status == TerrainLodColumnTileCacheReadStatus.Hit,
                    TimeSpan.FromSeconds(30));

                runtime.TrackChunk(edited!);
                edited![0, 4, 0] = 0;
                runtime.BeforeChunkSave(edited);
                Assert.Equal(TerrainLodTileAvailability.Pending,
                    runtime.GetSpatialCoverage(key, out _));
                Assert.Equal(TerrainLodColumnTileCacheReadStatus.Missing,
                    new TerrainLodColumnTileCacheStore(root, runtime.Identity)
                        .Read(key).Status);
                storage.SaveChunk(world, edited, null, 2);
                storage.FlushToDisk();
                var invalidation = Assert.Single(runtime.NotifyChunkSaved(edited));
                Assert.Equal(key, invalidation.Key);
                Assert.True(invalidation.Generation > 0);

                await WaitUntil(() => runtime.GetSpatialCoverage(key, out var tile) ==
                                      TerrainLodTileAvailability.Ready &&
                                      tile!.CanonicalHash != original!.CanonicalHash,
                    TimeSpan.FromSeconds(30));
                Assert.True(runtime.TryGetSpatialCoverage(key, out var replacement));
                replacementHash = replacement!.CanonicalHash;
                await WaitUntil(() =>
                {
                    var cached = new TerrainLodColumnTileCacheStore(root, runtime.Identity)
                        .Read(key);
                    return cached.Status == TerrainLodColumnTileCacheReadStatus.Hit &&
                           cached.Tile!.CanonicalHash == replacementHash;
                }, TimeSpan.FromSeconds(30));
            }

            using var reopened = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world,
                storedTerrain: storage);
            await WaitUntil(() => reopened.TryGetSpatialCoverage(key, out _),
                TimeSpan.FromSeconds(30));
            Assert.True(reopened.TryGetSpatialCoverage(key, out var cachedTile));
            Assert.Equal(replacementHash, cachedTile!.CanonicalHash);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Two_clients_keep_old_lod_until_latest_saved_generation_arrives()
    {
        var root = CreateTemporaryDirectory();
        var save = new DirectoryInfo(Path.Combine(root.FullName, "saved"));
        save.Create();
        try
        {
            var world = new FakeWorldContext();
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var storage = new RegionChunkStorage(save.FullName);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            Chunk? edited = null;
            for (var x = 0; x < 4; x++)
            for (var z = 0; z < 4; z++)
            {
                var chunk = Chunk(world, x, z);
                chunk[0, 4, 0] = stone;
                storage.SaveChunk(world, chunk, null, 1);
                if (x == 0 && z == 0) edited = chunk;
            }
            storage.FlushToDisk();
            using var runtime = new ServerTerrainLodRuntime(
                0, materials, root, world, storedTerrain: storage);
            var key = new TerrainLodTileKey(2, 0, 0);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.TryGetSpatialCoverage(key, out _),
                TimeSpan.FromSeconds(30)));
            Assert.True(runtime.TryGetSpatialCoverage(key, out var initial));
            var initialTile = Assert.IsType<TerrainLodColumnTile>(initial);

            using var first = new ClientTerrainLodRenderer(new LightTestWorld());
            using var second = new ClientTerrainLodRenderer(new LightTestWorld());
            var identity = runtime.Identity.CompatibilityFingerprint;
            static TerrainLodTileMessage WireTile(TerrainLodColumnTile tile,
                string cacheIdentity, long generation)
            {
                var outgoing = TerrainLodTileMessage.Of(
                    0, tile, cacheIdentity, generation);
                using MemoryStream stream = new();
                outgoing.Write(stream);
                stream.Position = 0;
                TerrainLodTileMessage incoming = new();
                incoming.Read(stream);
                return incoming;
            }
            static TerrainLodTileStatusMessage WireInvalidation(
                TerrainLodTileKey tile, string cacheIdentity, long generation)
            {
                TerrainLodTileStatusMessage outgoing = new()
                {
                    Dimension = 0,
                    CacheIdentity = cacheIdentity,
                    Tile = tile,
                    Generation = generation,
                    Status = TerrainLodTileStatus.Invalidated
                };
                using MemoryStream stream = new();
                outgoing.Write(stream);
                stream.Position = 0;
                TerrainLodTileStatusMessage incoming = new();
                incoming.Read(stream);
                return incoming;
            }

            var initialWire = WireTile(initialTile, identity, 0);
            first.ObserveRemoteSpatialTile(
                initialWire.Decode(), generation: initialWire.Generation);
            second.ObserveRemoteSpatialTile(
                initialWire.Decode(), generation: initialWire.Generation);
            Assert.Equal(initialTile.CanonicalHash, first.GetResidentSpatialSourceHash(key));
            Assert.Equal(initialTile.CanonicalHash, second.GetResidentSpatialSourceHash(key));
            runtime.TrackChunk(edited!);

            edited![0, 4, 0] = 0;
            runtime.BeforeChunkSave(edited);
            storage.SaveChunk(world, edited, null, 2);
            storage.FlushToDisk();
            var firstNotice = Assert.Single(runtime.NotifyChunkSaved(edited));
            var invalidation = WireInvalidation(key, identity, firstNotice.Generation);
            first.ObserveRemoteSpatialStatus(invalidation.Tile,
                invalidation.Status, invalidation.Generation);
            second.ObserveRemoteSpatialStatus(invalidation.Tile,
                invalidation.Status, invalidation.Generation);
            Assert.True(first.HasPendingRemoteRefresh(key));
            Assert.True(second.HasPendingRemoteRefresh(key));
            Assert.Equal(initialTile.CanonicalHash, first.GetResidentSpatialSourceHash(key));
            Assert.Equal(initialTile.CanonicalHash, second.GetResidentSpatialSourceHash(key));

            var camera = new Vector3D<double>(32, 80, 32);
            Assert.Contains(key, first.TakeRemoteSpatialRequests(camera, 0, 4, 16));
            Assert.Contains(key, second.TakeRemoteSpatialRequests(camera, 0, 4, 16));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.GetSpatialCoverage(key, out var tile) ==
                      TerrainLodTileAvailability.Ready &&
                      tile!.CanonicalHash != initialTile.CanonicalHash,
                TimeSpan.FromSeconds(30)));
            Assert.True(runtime.TryGetSpatialCoverage(key, out var firstReplacement));
            var firstReplacementTile = Assert.IsType<TerrainLodColumnTile>(firstReplacement);

            edited[1, 4, 0] = stone;
            runtime.BeforeChunkSave(edited);
            storage.SaveChunk(world, edited, null, 3);
            storage.FlushToDisk();
            var secondNotice = Assert.Single(runtime.NotifyChunkSaved(edited));
            Assert.True(secondNotice.Generation > firstNotice.Generation);
            invalidation = WireInvalidation(key, identity, secondNotice.Generation);
            first.ObserveRemoteSpatialStatus(invalidation.Tile,
                invalidation.Status, invalidation.Generation);
            second.ObserveRemoteSpatialStatus(invalidation.Tile,
                invalidation.Status, invalidation.Generation);

            var delayed = WireTile(firstReplacementTile, identity, firstNotice.Generation);
            first.ObserveRemoteSpatialTile(delayed.Decode(), generation: delayed.Generation);
            second.ObserveRemoteSpatialTile(delayed.Decode(), generation: delayed.Generation);
            Assert.True(first.HasPendingRemoteRefresh(key));
            Assert.True(second.HasPendingRemoteRefresh(key));
            Assert.Equal(initialTile.CanonicalHash, first.GetResidentSpatialSourceHash(key));
            Assert.Equal(initialTile.CanonicalHash, second.GetResidentSpatialSourceHash(key));

            Assert.True(SpinWait.SpinUntil(
                () => runtime.GetSpatialCoverage(key, out var tile) ==
                      TerrainLodTileAvailability.Ready &&
                      tile!.CanonicalHash != firstReplacementTile.CanonicalHash,
                TimeSpan.FromSeconds(30)));
            Assert.True(runtime.TryGetSpatialCoverage(key, out var latest));
            var latestTile = Assert.IsType<TerrainLodColumnTile>(latest);
            var latestWire = WireTile(latestTile, identity, secondNotice.Generation);
            first.ObserveRemoteSpatialTile(
                latestWire.Decode(), generation: latestWire.Generation);
            Assert.False(first.HasPendingRemoteRefresh(key));
            Assert.Equal(latestTile.CanonicalHash, first.GetResidentSpatialSourceHash(key));
            Assert.True(second.HasPendingRemoteRefresh(key));
            Assert.Equal(initialTile.CanonicalHash, second.GetResidentSpatialSourceHash(key));

            second.ObserveRemoteSpatialTile(
                latestWire.Decode(), generation: latestWire.Generation);
            Assert.False(second.HasPendingRemoteRefresh(key));
            Assert.Equal(latestTile.CanonicalHash, second.GetResidentSpatialSourceHash(key));
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData(512, 7, 289)]
    [InlineData(1024, 8, 297)]
    public void Explicit_scale_policy_controls_identity_and_fixture_depth(
        int horizonChunks,
        int expectedMaximumLevel,
        int expectedTiles)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var policy = TerrainLodSpatialPolicy.CreateForMaximumHorizon(horizonChunks);
            using var runtime = new ServerTerrainLodRuntime(
                0,
                TerrainLodMaterialCatalog.FromRuntime(world.Content),
                root,
                world,
                conversionCapacity: 2,
                spatialPolicy: policy);

            var tiles = runtime.PrepareUniformSpatialFixture(
                centerChunkX: 0,
                centerChunkZ: 20,
                nearDistanceChunks: 4,
                horizonDistanceChunks: horizonChunks,
                surfaceBlockProtocolId:
                    world.Content.Blocks.Get("omniblock:grass_block").Id);

            Assert.Equal(expectedMaximumLevel, runtime.Identity.MaximumSpatialLevel);
            Assert.Equal(expectedTiles, tiles);
            Assert.InRange(tiles, 1, TerrainLodScaleBudget.MaximumCoverageTiles);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Overlapping_fixture_centers_preserve_shared_tile_identity()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            using var runtime = new ServerTerrainLodRuntime(
                0, TerrainLodMaterialCatalog.FromRuntime(world.Content), root, world);
            var stone = world.Content.Blocks.Get("omniblock:stone").Id;
            var sharedKey = new TerrainLodTileKey(4, 1, 1);
            runtime.PrepareUniformSpatialFixture(0, 20, 4, 64, stone);
            Assert.True(runtime.TryGetSpatialCoverage(sharedKey, out var before));
            runtime.PrepareUniformSpatialFixture(0, 21, 4, 64, stone);
            Assert.True(runtime.TryGetSpatialCoverage(sharedKey, out var after));
            Assert.Equal(before!.CanonicalHash, after!.CanonicalHash);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static Chunk Chunk(FakeWorldContext world, int x, int z) =>
        new(world, new byte[ChuckFormat.ChunkSize], x, z);

    private sealed class PausingTerrainSourceStorage(
        Dictionary<(int X, int Z), TerrainLodSourceSnapshot> sources,
        ManualResetEventSlim blocked,
        ManualResetEventSlim release) : IChunkStorage
    {
        private int _reads;

        public void Replace(TerrainLodSourceSnapshot source) =>
            sources[(source.ChunkX, source.ChunkZ)] = source;

        public TerrainLodSourceSnapshot? ReadTerrainLodSource(
            int chunkX, int chunkZ, bool hasSkyLight)
        {
            if (Interlocked.Increment(ref _reads) == 8)
            {
                blocked.Set();
                if (!release.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Test did not release the saved-source read.");
            }
            return sources.GetValueOrDefault((chunkX, chunkZ));
        }

        public Chunk? LoadChunk(IWorldContext world, int chunkX, int chunkZ) =>
            throw new NotSupportedException();
        public ChunkSaveResult SaveChunk(
            IWorldContext world, Chunk chunk, Action? onSave, long sequence) =>
            throw new NotSupportedException();
        public void SaveEntities(IWorldContext world, Chunk chunk) =>
            throw new NotSupportedException();
        public void Tick() { }
        public void Flush() { }
        public void FlushToDisk() { }
    }

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var directory = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-server-terrain-lod-{Guid.NewGuid():N}"));
        directory.Create();
        return directory;
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(2);
        }
    }
}
