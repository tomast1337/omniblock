using System.Security.Cryptography;
using System.Text;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Server.Worlds;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Generation;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Tests.Worlds;

[Collection(ChunkGeneratorCharacterizationCollection.Name)]
public sealed partial class InactiveGenerationWorkspaceTests
{
    // Unlike the uniform scale fixtures, these go through the actual generator, decoration and
    // lighting. Keep the patch small: fidelity evidence is not a 1024-chunk performance claim.
    [Theory]
    [InlineData("default", -17, 15, false)]
    [InlineData("sky", -17, 15, false)]
    [InlineData("nether", -17, 15, false)]
    [InlineData("default", 0, 0, true)]
    public async Task TerrainLod_generated_columns_survive_reduction_cache_reopen_and_transport(
        string profile, int parentX, int parentZ, bool requireLiquid)
    {
        var root = Directory.CreateTempSubdirectory("omniblock-generated-lod-");
        try
        {
            var world = new SourceWorld(246813579L, profile);
            var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(parentX * 2 + 1, parentZ * 2 + 1);
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var key = new TerrainLodTileKey(1, parentX, parentZ);
            var leaves = new TerrainLodColumnTile[4];
            HashSet<int> surfaceHeights = [];
            var undergroundAir = 0;
            var liquidVoxels = 0;
            using (var runtime = new ServerTerrainLodRuntime(
                       world.Dimension.Id, materials, root, world, conversionCapacity: 8))
            {
                for (var i = 0; i < 4; i++)
                {
                    var child = key.Child(i);
                    var offline = batch.Get(child.X, child.Z);
                    var source = offline.CaptureTerrain().WithClimate(world.Dimension.BiomeSource);
                    var leaf = leaves[i] = TerrainLodColumnTile.BuildLeaf(source, materials);
                    Assert.NotNull(source.Lighting);
                    for (var x = 0; x < 16; x++)
                    for (var z = 0; z < 16; z++)
                    {
                        var top = -1;
                        for (var y = source.Height - 1; y >= 0; y--)
                        {
                            var expected = materials.Resolve(source.GetBlock(x, y, z), source.GetMetadata(x, y, z));
                            var actual = leaf[x, z].At(y);
                            var light = source.Lighting.GetLightLevels(child.X * 16 + x, y, child.Z * 16 + z, 0);
                            Assert.Equal(expected, actual.Material);
                            Assert.Equal(light.Block, actual.BlockLight);
                            Assert.Equal(light.Sky, actual.SkyLight);
                            if (!expected.IsAir && top < 0) top = y;
                            if (expected.IsAir && top >= 0) undergroundAir++;
                            if (expected.Geometry == TerrainLodGeometryClass.Liquid) liquidVoxels++;
                        }
                        surfaceHeights.Add(top);
                    }
                    runtime.SubmitOffline(offline);
                }

                await WaitForTerrainLod(() => runtime.TryGetSpatialCoverage(key, out _) &&
                    runtime.Snapshot().SpatialCache.Writes >= 5);
                Assert.True(runtime.TryGetSpatialCoverage(key, out var built));
                Assert.NotNull(built!.Climate);
                var expectedParent = TerrainLodColumnTile.BuildParent(key, leaves,
                    TerrainLodSpatialPolicy.CreateDefault().HorizontalSampleLevelForSpatialLevel(key.Level));
                Assert.Equal(expectedParent.CanonicalHash, built.CanonicalHash);
                // The near parent retains 1:1 columns, not merely one highest surface per column.
                Assert.Equal(0, built.HorizontalSampleLevel);
                for (var i = 0; i < 4; i++)
                for (var x = 0; x < 16; x++)
                for (var z = 0; z < 16; z++)
                {
                    var child = key.Child(i);
                    Assert.Equal(leaves[i][x, z].Spans,
                        built[(child.X - key.X * 2) * 16 + x, (child.Z - key.Z * 2) * 16 + z].Spans);
                }
                Assert.Equal(0, runtime.Snapshot().OfflineSnapshotsDropped);
            }

            // Guard the fixture itself: a flat synthetic slab cannot satisfy this test.
            Assert.True(undergroundAir > 0, $"{profile}: no cave/overhang air was exercised");
            if (profile != "nether") Assert.True(surfaceHeights.Count > 1, $"{profile}: fixture was flat");
            if (requireLiquid) Assert.True(liquidVoxels > 0, $"{profile}: no liquid was exercised");

            using var reopened = new ServerTerrainLodRuntime(world.Dimension.Id, materials, root, world);
            await WaitForTerrainLod(() => reopened.TryGetSpatialPayload(key, out _));
            Assert.True(reopened.TryGetSpatialPayload(key, out var payload));
            var transported = TerrainLodTileMessage.FromCompressed(world.Dimension.Id, payload!).Decode();
            Assert.NotNull(transported.Climate);
            var expectedHash = TerrainLodColumnTile.BuildParent(key, leaves, 0).CanonicalHash;
            Assert.Equal(expectedHash, transported.CanonicalHash);
            Assert.True(reopened.Snapshot().SpatialCache.ReadHits > 0);
            Assert.Equal(0, reopened.Snapshot().TrackedChunks);
            Assert.Equal(0, reopened.Snapshot().OfflineSnapshotsSubmitted);
            Assert.False(world.Chunks.IsChunkLoaded(parentX * 2, parentZ * 2));

            // An absent neighbor must stay missing, never be filled by extrapolating this patch.
            var absent = new TerrainLodTileKey(1, 100, 100);
            await WaitForTerrainLod(() => reopened.GetSpatialCoverage(absent, out _) == TerrainLodTileAvailability.Missing);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData(246813579L, "flat")]
    [InlineData(123456789L, "default")]
    public void TerrainLod_generated_cache_cannot_leak_into_another_generator_or_seed(long seed, string profile)
    {
        var root = Directory.CreateTempSubdirectory("omniblock-generated-lod-identity-");
        try
        {
            var original = new SourceWorld(246813579L);
            var materials = TerrainLodMaterialCatalog.FromRuntime(original.Content);
            var source = new InactiveGenerationWorkspace(original)
                .GenerateCompletedNeighborhood(0, 0).Get(0, 0).CaptureTerrain();
            var leaf = TerrainLodColumnTile.BuildLeaf(source, materials);
            var originalIdentity = TerrainLodCacheIdentity.FromWorld(original, materials, root.FullName);
            var store = new TerrainLodColumnTileCacheStore(root, originalIdentity);
            Assert.Equal(TerrainLodColumnTileCacheWriteStatus.Written, store.Write(leaf));

            // Even reusing the physical cache directory cannot turn a flat world's cache into
            // valid terrain for a regular world (or vice versa).
            var other = new SourceWorld(seed, profile);
            var otherIdentity = TerrainLodCacheIdentity.FromWorld(other, materials, root.FullName);
            var rejected = new TerrainLodColumnTileCacheStore(root, otherIdentity).Read(leaf.Key);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Incompatible, rejected.Status);
            Assert.Null(rejected.Tile);
            // A rejected read must not destroy a still-valid record for the original world.
            Assert.Equal(leaf.CanonicalHash, store.Read(leaf.Key).Tile!.CanonicalHash);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData("default")]
    [InlineData("sky")]
    [InlineData("nether")]
    public async Task TerrainLod_generated_coarse_parent_replaces_edited_descendant_and_persists_it(string profile)
    {
        var root = Directory.CreateTempSubdirectory("omniblock-generated-lod-edit-");
        try
        {
            var world = new SourceWorld(246813579L, profile);
            var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(1, 1);
            var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
            var key = new TerrainLodTileKey(2, 0, 0);
            var policy = TerrainLodSpatialPolicy.CreateDefault();
            Dictionary<TerrainLodTileKey, TerrainLodColumnTile> leaves = [];
            foreach (var snapshot in batch.Chunks)
            {
                var leaf = TerrainLodColumnTile.BuildLeaf(
                    snapshot.CaptureTerrain().WithClimate(world.Dimension.BiomeSource), materials);
                leaves.Add(leaf.Key, leaf);
            }

            TerrainLodColumnTile ExpectedParent()
            {
                var children = Enumerable.Range(0, 4).Select(i =>
                {
                    var child = key.Child(i);
                    return TerrainLodColumnTile.BuildParent(child,
                        Enumerable.Range(0, 4).Select(j => leaves[child.Child(j)]).ToArray(),
                        policy.HorizontalSampleLevelForSpatialLevel(1));
                }).ToArray();
                return TerrainLodColumnTile.BuildParent(key, children,
                    policy.HorizontalSampleLevelForSpatialLevel(2));
            }

            string editedHash;
            using (var runtime = new ServerTerrainLodRuntime(
                       world.Dimension.Id, materials, root, world, conversionCapacity: 32))
            {
                foreach (var snapshot in batch.Chunks.Reverse()) runtime.SubmitOffline(snapshot);
                var original = ExpectedParent();
                Assert.Equal(0, original.HorizontalSampleLevel); // The remotely usable near tier is now block-scale.
                await WaitForTerrainLod(() => runtime.TryGetSpatialCoverage(key, out var tile) &&
                    tile!.CanonicalHash == original.CanonicalHash);

                var chunk = batch.Get(0, 0).Materialize(world);
                // Change a 2x2 patch, checking both content and canonical hash after rebuilding
                // the remotely usable parent from an edited descendant.
                var stone = world.Content.Blocks.Get("omniblock:stone").Id;
                for (var x = 0; x < 2; x++)
                for (var z = 0; z < 2; z++)
                    chunk[x, 100, z] = chunk[x, 100, z] == stone ? 0 : stone;
                var edited = InactiveChunkSnapshot.Capture(chunk, world);
                var editedLeaf = TerrainLodColumnTile.BuildLeaf(
                    edited.CaptureTerrain().WithClimate(world.Dimension.BiomeSource), materials);
                leaves[editedLeaf.Key] = editedLeaf;
                var expected = ExpectedParent();
                editedHash = expected.CanonicalHash;
                Assert.NotEqual(original.CanonicalHash, editedHash);
                Assert.NotEqual(original[0, 0].At(100).Material, expected[0, 0].At(100).Material);
                runtime.SubmitOffline(edited);
                await WaitForTerrainLod(() => runtime.TryGetSpatialCoverage(key, out var tile) &&
                    tile!.CanonicalHash == editedHash);
                Assert.Equal(0, runtime.Snapshot().OfflineSnapshotsDropped);
            }

            using var reopened = new ServerTerrainLodRuntime(world.Dimension.Id, materials, root, world);
            await WaitForTerrainLod(() => reopened.TryGetSpatialPayload(key, out _));
            Assert.True(reopened.TryGetSpatialPayload(key, out var payload));
            Assert.Equal(editedHash,
                TerrainLodTileMessage.FromCompressed(world.Dimension.Id, payload!).Decode().CanonicalHash);
            Assert.Equal(0, reopened.Snapshot().TrackedChunks);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static async Task WaitForTerrainLod(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Generated terrain LOD pipeline did not settle.");
            await Task.Delay(5);
        }
    }

    public static TheoryData<string, string> GoldenInactiveNeighborhoods => new()
    {
        { "default", "3243eec63e6652fdb476fbad9f04049475e90c6afe688aedd696de1cd9fac709" },
        { "flat", "e10fc05d6ac7cbd6e614880cb404a2082f8aafcd286aa4368a1c8b0d25d06206" },
        { "sky", "df577d0336b57a4a68f5bef55735df492b45c3750b01dafd0a58bf1447106a2c" },
        { "nether", "dfcb7e8e43c3bae5b8ba44a904f182b2f08b854ace29cb51f8e77aa55c332bc5" }
    };

    public static TheoryData<string, string> GoldenOverlappingInactiveRegions => new()
    {
        { "default", "d8919fbab609fe7633917bd53fcf6e3a9ba8ba1eb682befd4ac128e9b682440f" },
        { "flat", "8d10226948fa2e9ca654fd08c185510a316ae92cea906030a6585b1164cf7dcb" },
        { "sky", "0ec98af116718511df4de18195352cfcac64631f46f36776f0c46acade3544e7" },
        { "nether", "d63ec9920646ffa8d9157685a198efcd286c241ad91cf55b2976b214b1a2d033" }
    };

    [Fact]
    public void Stored_ticks_do_not_reach_the_world_before_chunk_activation()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("stone");
        var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], 2, -3);

        chunk.QueueActivationTick(33, 64, -47, stone.Id, 7);

        Assert.Equal(0, world.TickScheduler.Count);
        Assert.Equal(1, chunk.PendingActivationTickCount);
        chunk.Load();
        Assert.Equal(1, world.TickScheduler.Count);
        Assert.Equal(0, chunk.PendingActivationTickCount);
    }

    [Fact]
    public void Completed_workspace_does_not_publish_entities_ticks_or_chunks_to_source_world()
    {
        var source = new SourceWorld(246813579L);
        var entityCount = source.Entities.Entities.Count;
        var tickCount = source.TickScheduler.Count;
        var workspace = new InactiveGenerationWorkspace(source);

        var batch = workspace.GenerateCompletedNeighborhood(0, 0);

        Assert.Equal(16, batch.Chunks.Count);
        Assert.Equal(entityCount, source.Entities.Entities.Count);
        Assert.Equal(tickCount, source.TickScheduler.Count);
        Assert.False(source.Chunks.IsChunkLoaded(0, 0));

        var materialized = batch.Get(0, 0).Materialize(source);
        Assert.False(materialized.Loaded);
        Assert.Same(source, materialized.World);
        Assert.False(source.Chunks.IsChunkLoaded(0, 0));
        Assert.Equal(entityCount, source.Entities.Entities.Count);
        Assert.Equal(tickCount, source.TickScheduler.Count);
    }

    [Fact]
    public void Workspace_honors_cancellation_before_starting_an_indivisible_stage()
    {
        var source = new SourceWorld(1L);
        var workspace = new InactiveGenerationWorkspace(source);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            workspace.GenerateCompletedNeighborhood(0, 0, cancellation.Token));
        Assert.False(source.Chunks.IsChunkLoaded(0, 0));
    }

    [Fact]
    public void Region_save_acknowledges_only_after_the_output_stream_is_committed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-save-ack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var world = new SourceWorld(1L);
            var chunk = world.Generator.GetChunk(0, 0);
            var callbackCalled = false;
            var storage = new RegionChunkStorage(root);

            var result = storage.SaveChunk(world, chunk, () => callbackCalled = true, 1);

            Assert.True(callbackCalled);
            Assert.True(result.SizeDeltaBytes > 0);
            Assert.NotNull(storage.LoadChunk(world, 0, 0));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Missing_chunk_probe_does_not_create_an_empty_region_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storage = new RegionChunkStorage(root);

            Assert.False(storage.ContainsChunk(1024, -1024));
            Assert.False(Directory.Exists(Path.Combine(root, "region")));
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Completed_batch_is_durably_saved_reopened_and_stays_inactive_until_load()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-inactive-commit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var world = new SourceWorld(246813579L);
            var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
            var expected = batch.Get(0, 0).Materialize(world);
            var storage = new RegionChunkStorage(root);

            var commit = batch.SaveDurably(world, storage, firstSequence: 100);

            Assert.Equal(16, commit.Chunks.Count);
            Assert.True(commit.SizeDeltaBytes > 0);
            var reopened = new RegionChunkStorage(root).LoadChunk(world, 0, 0);
            Assert.NotNull(reopened);
            Assert.False(reopened.Loaded);
            Assert.Equal(expected.Blocks, reopened.Blocks);
            Assert.Equal(expected.Meta.Bytes, reopened.Meta.Bytes);
            Assert.Equal(expected.HeightMap, reopened.HeightMap);
            Assert.Equal(expected.TerrainPopulated, reopened.TerrainPopulated);

            reopened.Load();
            Assert.True(reopened.Loaded);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stored_dependencies_are_used_for_reads_but_never_emitted_for_overwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-inactive-existing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storage = new RegionChunkStorage(root);
            var world = new SourceWorld(246813579L, storage: new ChunkBackedStorage(storage));
            var stored = world.Generator.GetChunk(-1, -1);
            var marker = world.Content.Blocks.Get("diamond_block").Id;
            stored.SetBlock(1, 100, 1, marker, 0, false);
            storage.SaveChunk(world, stored, null, 0);
            storage.FlushToDisk();

            var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);

            Assert.Equal(15, batch.Chunks.Count);
            Assert.DoesNotContain(batch.Chunks, chunk => chunk.X == -1 && chunk.Z == -1);
            var reopened = storage.LoadChunk(world, -1, -1);
            Assert.NotNull(reopened);
            Assert.Equal(marker, reopened.GetBlockId(1, 100, 1));
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Dependency_saved_by_one_batch_is_decorated_when_it_becomes_a_later_target()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-inactive-sequence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storage = new RegionChunkStorage(root);
            var world = new SourceWorld(246813579L, storage: new ChunkBackedStorage(storage));
            var first = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
            first.SaveDurably(world, storage);
            var dependency = storage.LoadChunk(world, 1, 0);
            Assert.NotNull(dependency);
            Assert.False(dependency.TerrainPopulated);

            var second = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(1, 0);

            Assert.Contains(new ChunkPos(1, 0), second.WritableStoredTargets);
            Assert.DoesNotContain(new ChunkPos(1, 0), second.SkippedTargets);
            Assert.Contains(second.Chunks, chunk => chunk.X == 1 && chunk.Z == 0);
            second.SaveDurably(world, storage);
            Assert.True(storage.LoadChunk(world, 1, 0)!.TerrainPopulated);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Adjacent_inactive_batches_preserve_cross_border_decoration()
    {
        var root = Directory.CreateTempSubdirectory("omniblock-inactive-border-");
        try
        {
            var storage = new RegionChunkStorage(root.FullName);
            var world = new SourceWorld(246813579L, storage: new ChunkBackedStorage(storage));
            var first = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
            first.SaveDurably(world, storage);
            var second = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(1, 0);
            Assert.Contains(new ChunkPos(1, 1), second.WritableStoredTargets);
            second.SaveDurably(world, storage);

            var expectedWorld = new SourceWorld(246813579L);
            var expected = new InactiveGenerationWorkspace(expectedWorld)
                .GenerateCompletedRegion([new ChunkPos(0, 0), new ChunkPos(1, 0)]);
            foreach (var chunk in expected.Chunks)
            {
                var saved = storage.LoadChunk(world, chunk.X, chunk.Z);
                Assert.NotNull(saved);
                var expectedChunk = chunk.Materialize(expectedWorld);
                var expectedBlocks = expectedChunk.Blocks;
                var firstDifference = Enumerable.Range(0, expectedBlocks.Length)
                    .FirstOrDefault(index => expectedBlocks[index] != saved.Blocks[index], -1);
                if (firstDifference >= 0)
                    Assert.Fail($"Chunk {chunk.X},{chunk.Z} differs at block index {firstDifference}: " +
                        $"expected {expectedBlocks[firstDifference]}, saved {saved.Blocks[firstDifference]}.");
                Assert.Equal(expectedChunk.Meta.Bytes, saved.Meta.Bytes);
            }
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Failed_batch_write_identifies_chunk_and_never_reports_a_durable_commit()
    {
        var world = new SourceWorld(1L);
        var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
        var storage = new RecordingChunkStorage(failWriteNumber: 2);

        var error = Assert.Throws<InactiveGenerationCommitException>(() =>
            batch.SaveDurably(world, storage));

        Assert.Equal(InactiveGenerationCommitStage.WriteChunk, error.Stage);
        Assert.Equal(-1, error.ChunkX);
        Assert.Equal(0, error.ChunkZ);
        Assert.Equal(1, error.CompletedWrites);
        Assert.False(storage.FlushedToDisk);
    }

    [Fact]
    public void Failed_durable_flush_is_distinct_from_completed_chunk_writes()
    {
        var world = new SourceWorld(1L);
        var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
        var storage = new RecordingChunkStorage(failFlush: true);

        var error = Assert.Throws<InactiveGenerationCommitException>(() =>
            batch.SaveDurably(world, storage));

        Assert.Equal(InactiveGenerationCommitStage.Flush, error.Stage);
        Assert.Null(error.ChunkX);
        Assert.Null(error.ChunkZ);
        Assert.Equal(16, error.CompletedWrites);
        Assert.True(storage.FlushedToDisk);
    }

    [Theory]
    [MemberData(nameof(GoldenInactiveNeighborhoods))]
    public void Single_target_inactive_decoration_preserves_the_shipped_fingerprint(
        string profile,
        string expected)
    {
        var world = new SourceWorld(246813579L, profile);
        var batch = new InactiveGenerationWorkspace(world)
            .GenerateCompletedNeighborhood(-33, 31);

        Assert.Equal(expected, Fingerprint(batch, world));
    }

    [Theory]
    [MemberData(nameof(GoldenOverlappingInactiveRegions))]
    public void Overlapping_targets_share_dependencies_and_ignore_input_completion_order(
        string profile,
        string expected)
    {
        ChunkPos[] forwardTargets = [new(0, 0), new(1, 0), new(0, 0)];
        ChunkPos[] reversedTargets = [new(1, 0), new(0, 0)];
        var forwardWorld = new SourceWorld(0x2468_1357_7654_321L, profile);
        var reversedWorld = new SourceWorld(0x2468_1357_7654_321L, profile);

        var forward = new InactiveGenerationWorkspace(forwardWorld)
            .GenerateCompletedRegion(forwardTargets);
        var reversed = new InactiveGenerationWorkspace(reversedWorld)
            .GenerateCompletedRegion(reversedTargets);

        Assert.Equal([new ChunkPos(0, 0), new ChunkPos(1, 0)], forward.DecoratedTargets);
        Assert.Equal(20, forward.Chunks.Count);
        var actual = Fingerprint(forward, forwardWorld);
        Assert.Equal(actual, Fingerprint(reversed, reversedWorld));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Unknown_generator_provider_fails_before_inactive_work_starts()
    {
        var source = new FakeWorldContext();
        source.Properties.TerrainType = new WorldType("example:custom", "example:custom");

        var error = Assert.Throws<NotSupportedException>(() =>
            new InactiveGenerationWorkspace(source));

        Assert.Contains("example:custom", error.Message);
        Assert.Contains("deterministic inactive-decoration", error.Message);
    }

    [Fact]
    public void Instant_fall_scope_is_nested_and_restored()
    {
        FallingBlockBehavior.FallInstantly = false;
        using (FallingBlockBehavior.BeginInstantFallScope())
        {
            Assert.True(FallingBlockBehavior.FallInstantly);
            using (FallingBlockBehavior.BeginInstantFallScope())
                Assert.True(FallingBlockBehavior.FallInstantly);
            Assert.True(FallingBlockBehavior.FallInstantly);
        }

        Assert.False(FallingBlockBehavior.FallInstantly);
    }

    [Fact]
    public void Instant_fall_scope_is_restored_when_decoration_throws()
    {
        FallingBlockBehavior.FallInstantly = false;

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = FallingBlockBehavior.BeginInstantFallScope();
            Assert.True(FallingBlockBehavior.FallInstantly);
            throw new InvalidOperationException("fixture");
        }));

        Assert.False(FallingBlockBehavior.FallInstantly);
    }

    private static string Fingerprint(InactiveGenerationBatch batch, IWorldContext world)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var snapshot in batch.Chunks.OrderBy(static chunk => chunk.X).ThenBy(static chunk => chunk.Z))
        {
            var chunk = snapshot.Materialize(world);
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"{chunk.X},{chunk.Z}:{chunk.TerrainPopulated}:{chunk.BlockEntities.Count}:"));
            hash.AppendData(chunk.Blocks);
            hash.AppendData(chunk.Meta.Bytes);
            hash.AppendData(chunk.HeightMap);
            hash.AppendData(chunk.SkyLight.Bytes);
            hash.AppendData(chunk.BlockLight.Bytes);
            foreach (var blockEntity in chunk.BlockEntities.Values
                         .OrderBy(static entity => entity.X)
                         .ThenBy(static entity => entity.Y)
                         .ThenBy(static entity => entity.Z))
                hash.AppendData(Encoding.UTF8.GetBytes(
                    $"{blockEntity.GetType().FullName}@{blockEntity.X},{blockEntity.Y},{blockEntity.Z};"));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed class SourceWorld : World
    {
        public SourceWorld(long seed, string profile = "default", IWorldStorage? storage = null)
            : base(storage ?? new MemoryStorage(), "inactive-source",
                new WorldSettings(seed, ResolveWorldType(profile)),
                ResolveDimension(profile), ContentRuntime.Current)
        {
            Generator = Dimension.CreateChunkGenerator();
        }

        public MemorySource Chunks { get; private set; } = null!;
        public IChunkSource Generator { get; }
        protected override IChunkSource CreateChunkCache() => Chunks = new MemorySource(this);

        private static WorldType ResolveWorldType(string profile) =>
            ContentRuntime.Current.WorldTypes.Get(profile == "nether" ? "default" : profile);

        private static Dimension? ResolveDimension(string profile) =>
            profile == "nether" ? Dimension.FromId(-1, ContentRuntime.Current) : null;
    }

    private sealed class MemorySource(IWorldContext world) : IChunkSource
    {
        private readonly Dictionary<(int X, int Z), Chunk> _chunks = [];
        public bool IsChunkLoaded(int x, int z) => _chunks.ContainsKey((x, z));
        public Chunk GetChunk(int x, int z) => _chunks.TryGetValue((x, z), out var chunk)
            ? chunk
            : new EmptyChunk(world, new byte[ChuckFormat.ChunkSize], x, z);
        public Chunk LoadChunk(int x, int z) => GetChunk(x, z);
        public void DecorateTerrain(IChunkSource source, int x, int z) { }
        public bool Save(bool saveEntities, LoadingDisplay? display) => true;
        public bool Tick() => false;
        public bool CanSave() => false;
        public string GetDebugInfo() => nameof(MemorySource);
    }

    private sealed class MemoryStorage : IWorldStorage
    {
        public WorldProperties? LoadProperties() => null;
        public void CheckSessionLock() { }
        public IChunkStorage? GetChunkStorage(Dimension dimension) => null;
        public void Save(WorldProperties properties, List<EntityPlayer> players) { }
        public void Save(WorldProperties properties) { }
        public void ForceSave() { }
        public IPlayerStorage? GetPlayerStorage() => null;
        public FileInfo? GetWorldPropertiesFile(string name) => null;
    }

    private sealed class ChunkBackedStorage(IChunkStorage chunks) : IWorldStorage
    {
        public WorldProperties? LoadProperties() => null;
        public void CheckSessionLock() { }
        public IChunkStorage GetChunkStorage(Dimension dimension) => chunks;
        public void Save(WorldProperties properties, List<EntityPlayer> players) { }
        public void Save(WorldProperties properties) { }
        public void ForceSave() { }
        public IPlayerStorage? GetPlayerStorage() => null;
        public FileInfo? GetWorldPropertiesFile(string name) => null;
    }

    private sealed class RecordingChunkStorage(int failWriteNumber = -1, bool failFlush = false)
        : IChunkStorage
    {
        private int _writes;
        public bool FlushedToDisk { get; private set; }

        public Chunk? LoadChunk(IWorldContext world, int chunkX, int chunkZ) => null;

        public ChunkSaveResult SaveChunk(IWorldContext world, Chunk chunk, Action? onSave, long sequence)
        {
            _writes++;
            if (_writes == failWriteNumber) throw new IOException("Injected write failure.");
            onSave?.Invoke();
            return new ChunkSaveResult(1);
        }

        public void SaveEntities(IWorldContext world, Chunk chunk) { }
        public void Tick() { }
        public void Flush() { }

        public void FlushToDisk()
        {
            FlushedToDisk = true;
            if (failFlush) throw new IOException("Injected flush failure.");
        }
    }
}
