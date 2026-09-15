using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodCacheStoreTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x707070),
        new TerrainLodMaterialDefinition(2, "example:water",
            TerrainLodGeometryClass.Liquid, false, 0x4040FF)
    ]);

    [Fact]
    public void Record_round_trips_stable_materials_topology_and_hash()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = Store(root);
            var expected = Result(3, -7, 42);

            Assert.Equal(TerrainLodCacheWriteStatus.Written, store.Write(expected));
            var read = store.Read(3, -7, 42);

            Assert.Equal(TerrainLodCacheReadStatus.Hit, read.Status);
            Assert.NotNull(read.Hierarchy);
            Assert.Equal(expected.Hierarchy.CanonicalHash, read.Hierarchy.CanonicalHash);
            Assert.Equal("example:water",
                read.Hierarchy.Levels[0][0, 0, 1].Primary.BlockId.ToString());
            Assert.Equal(expected.Hierarchy.Levels[1][0, 0, 0].OccupancyMask,
                read.Hierarchy.Levels[1][0, 0, 0].OccupancyMask);
            Assert.NotNull(read.Lighting);
            Assert.Equal(new LightLevels(11, 6),
                read.Lighting.GetLightLevels(3 * 16 + 1, 70, -7 * 16 + 2, 0));
            Assert.Equal(1, store.Snapshot().ReadHits);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Interrupted_atomic_replacement_preserves_the_previous_record()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Store(root).Write(Result(1, 2, 10));
            var interrupted = new TerrainLodCacheStore(
                root,
                Identity(),
                1024 * 1024,
                1024 * 1024,
                stage =>
                {
                    if (stage == TerrainLodCacheWriteStage.TemporaryDurable)
                        throw new IOException("simulated interruption");
                });

            Assert.Throws<IOException>(() => interrupted.Write(Result(1, 2, 11)));

            var reopened = Store(root);
            Assert.Equal(TerrainLodCacheReadStatus.Hit, reopened.Read(1, 2, 10).Status);
            Assert.Equal(TerrainLodCacheReadStatus.StaleTerrain,
                reopened.Read(1, 2, 11).Status);
            Assert.Empty(root.EnumerateFiles("*.tmp-*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Stale_incompatible_and_corrupt_records_are_safe_cache_misses()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = Store(root);
            store.Write(Result(-1, -33, 7));

            Assert.Equal(TerrainLodCacheReadStatus.StaleTerrain,
                store.Read(-1, -33, 8).Status);
            Assert.Equal(TerrainLodCacheReadStatus.StaleTerrain,
                store.Read(-1, -33, 7, "different-source").Status);
            var incompatible = new TerrainLodCacheStore(
                root,
                Identity() with { ContentFingerprint = "other-content" },
                1024 * 1024,
                1024 * 1024);
            Assert.Equal(TerrainLodCacheReadStatus.Incompatible,
                incompatible.Read(-1, -33, 7).Status);

            var path = Assert.Single(root.EnumerateFiles("*.olod", SearchOption.AllDirectories));
            var bytes = File.ReadAllBytes(path.FullName);
            bytes[bytes.Length / 2] ^= 0x40;
            File.WriteAllBytes(path.FullName, bytes);
            var corrupt = store.Read(-1, -33, 7);

            Assert.Equal(TerrainLodCacheReadStatus.Corrupt, corrupt.Status);
            Assert.Null(corrupt.Hierarchy);
            Assert.Equal(1, store.Snapshot().CorruptReads);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Disk_budget_evicts_old_cache_records_deterministically()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var initial = Store(root);
            initial.Write(Result(0, 0, 1));
            var recordBytes = initial.Snapshot().CurrentBytes;
            var bounded = new TerrainLodCacheStore(
                root,
                Identity(),
                recordBytes * 2,
                recordBytes * 2);

            bounded.Write(Result(1, 0, 1));
            bounded.Write(Result(2, 0, 1));

            var snapshot = bounded.Snapshot();
            Assert.Equal(2, snapshot.EntryCount);
            Assert.True(snapshot.CurrentBytes <= snapshot.MaxBytes);
            Assert.Equal(1, snapshot.Evictions);
            Assert.Equal(TerrainLodCacheReadStatus.Missing, bounded.Read(0, 0, 1).Status);
            Assert.Equal(TerrainLodCacheReadStatus.Hit, bounded.Read(1, 0, 1).Status);
            Assert.Equal(TerrainLodCacheReadStatus.Hit, bounded.Read(2, 0, 1).Status);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Oversize_record_is_rejected_without_replacing_existing_data()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var initial = Store(root);
            initial.Write(Result(4, 5, 1));
            var recordBytes = initial.Snapshot().CurrentBytes;
            var bounded = new TerrainLodCacheStore(
                root,
                Identity(),
                recordBytes - 1,
                recordBytes - 1);

            Assert.Equal(TerrainLodCacheWriteStatus.RejectedRecordTooLarge,
                bounded.Write(Result(4, 5, 2)));
            Assert.Equal(TerrainLodCacheReadStatus.Hit, initial.Read(4, 5, 1).Status);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Cache_writer_retries_the_same_ready_result_after_transient_io_failure()
    {
        var attempts = 0;
        using var conversions = new TerrainLodConversionService(0, Materials, capacity: 2);
        conversions.Submit(Source(8, 9, 3));
        await WaitUntil(() => conversions.Snapshot().Ready == 1);
        var writer = new TerrainLodCacheWriter(conversions, _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new IOException("disk temporarily unavailable");
            return TerrainLodCacheWriteStatus.Written;
        });

        Assert.Equal(0, writer.Drain(1));
        Assert.Equal(1, conversions.Snapshot().Ready);
        Assert.Equal(1, writer.Drain(1));

        Assert.Equal(0, conversions.Snapshot().OwnedChunks);
        Assert.Equal(2, writer.Snapshot().WriteAttempts);
        Assert.Equal(1, writer.Snapshot().Failures);
        Assert.Equal(1, writer.Snapshot().Written);
    }

    [Fact]
    public async Task Cache_aware_conversion_reuses_matching_source_and_lighting()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = Store(root);
            var expected = Result(6, -4, 3);
            store.Write(expected);
            using var conversions = new TerrainLodConversionService(
                0, Materials, store, capacity: 2);

            conversions.Submit(Source(6, -4, 3));
            TerrainLodConversionResult? actual = null;
            await WaitUntil(() => conversions.TryTakeCompleted(out actual));

            Assert.False(actual!.RequiresPersistence);
            Assert.Equal(expected.Hierarchy.CanonicalHash, actual.Hierarchy.CanonicalHash);
            Assert.Equal(new LightLevels(11, 6),
                actual.Lighting!.GetLightLevels(6 * 16 + 1, 70, -4 * 16 + 2, 0));
            Assert.Equal(1, store.Snapshot().ReadHits);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Async_cache_writer_persists_without_blocking_the_submitter()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var store = Store(root);
            var result = Result(-3, 8, 5);
            using var writer = new TerrainLodAsyncCacheWriter(store, capacity: 2);

            Assert.True(writer.TrySubmit(result));
            await WaitUntil(() => writer.Snapshot().Written == 1);

            Assert.Equal(0, writer.Snapshot().Queued);
            Assert.Equal(TerrainLodCacheReadStatus.Hit,
                store.Read(-3, 8, 5, result.SourceFingerprint).Status);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Material_rules_fingerprint_is_order_independent_and_changes_with_descriptors()
    {
        var reversed = new TerrainLodMaterialCatalog(
        [
            new TerrainLodMaterialDefinition(2, "example:water",
                TerrainLodGeometryClass.Liquid, false, 0x4040FF),
            new TerrainLodMaterialDefinition(1, "example:stone",
                TerrainLodGeometryClass.Opaque, true, 0x707070)
        ]);
        var changed = new TerrainLodMaterialCatalog(
        [
            new TerrainLodMaterialDefinition(1, "example:stone",
                TerrainLodGeometryClass.Cutout, false, 0x707070),
            new TerrainLodMaterialDefinition(2, "example:water",
                TerrainLodGeometryClass.Liquid, false, 0x4040FF)
        ]);

        Assert.Equal(Materials.RulesFingerprint, reversed.RulesFingerprint);
        Assert.NotEqual(Materials.RulesFingerprint, changed.RulesFingerprint);
    }

    private static TerrainLodCacheStore Store(DirectoryInfo root) =>
        new(root, Identity(), 1024 * 1024, 1024 * 1024);

    private static TerrainLodCacheIdentity Identity() => new(
        "world-fingerprint",
        0,
        "content-fingerprint",
        "generator-fingerprint",
        TerrainLodHierarchy.ReductionSchemaVersion,
        Materials.RulesFingerprint);

    private static TerrainLodConversionResult Result(int x, int z, long revision)
    {
        var source = Source(x, z, revision);
        return new TerrainLodConversionResult(
            0,
            x,
            z,
            revision,
            TerrainLodReducer.Build(
                source, Materials, TerrainLodReductionStrategy.SurfacePreserving),
            source.Lighting,
            SourceFingerprint: source.SourceFingerprint);
    }

    private static TerrainLodSourceSnapshot Source(int x, int z, long revision)
    {
        byte[] blocks =
        [
            1, 1,
            2, 0,
            1, 1,
            1, 1
        ];
        var sky = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        var block = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        sky.SetNibble(1, 70, 2, 11);
        block.SetNibble(1, 70, 2, 6);
        var lighting = new TerrainLodLightingSnapshot(
            x, z, revision, sky.Bytes, block.Bytes, hasSkyLight: true);
        return new TerrainLodSourceSnapshot(
            x, z, 2, 2, 2, blocks, new byte[blocks.Length], revision, lighting);
    }

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var directory = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-terrain-lod-{Guid.NewGuid():N}"));
        directory.Create();
        return directory;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(1);
        }
    }
}
