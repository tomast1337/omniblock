using System.Buffers.Binary;
using System.Security.Cryptography;
using OmniBlock.Network.Messages;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodColumnTileCacheStoreTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x707070),
        new TerrainLodMaterialDefinition(2, "example:water",
            TerrainLodGeometryClass.Liquid, false, 0x4050FF)
    ]);

    [Fact]
    public void Parent_tile_round_trips_canonical_columns_palette_and_identity()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var key = new TerrainLodTileKey(1, -2, 3);
            var children = new[]
            {
                Leaf(key.Child(0), 1, 11),
                Leaf(key.Child(1), 2, 12),
                Leaf(key.Child(2), 2, 13),
                Leaf(key.Child(3), 1, 14)
            };
            var expected = TerrainLodColumnTile.BuildParent(
                key, children, horizontalSampleLevel: 0);
            var store = Store(root);

            Assert.Equal(TerrainLodColumnTileCacheWriteStatus.Written,
                store.Write(expected));
            var read = store.Read(key);

            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Hit, read.Status);
            var actual = Assert.IsType<TerrainLodColumnTile>(read.Tile);
            Assert.Equal(expected.CanonicalHash, actual.CanonicalHash);
            Assert.Equal(expected.InputHashes, actual.InputHashes);
            Assert.Equal(32, actual.Width);
            Assert.Equal("example:stone", actual[0, 0].Spans[0].Material.BlockId.ToString());
            Assert.Equal(TerrainLodGeometryClass.Liquid,
                actual[31, 0].Spans[0].Material.Geometry);
            Assert.Equal(0x4050FFu, actual[31, 0].Spans[0].Material.MapColor);
            Assert.Equal(1, store.Snapshot().ReadHits);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Portable_message_round_trips_parent_without_disk_identity()
    {
        var key = new TerrainLodTileKey(1, 2, -4);
        var expected = TerrainLodColumnTile.BuildParent(key,
        [
            Leaf(key.Child(0), 1, 31),
            Leaf(key.Child(1), 2, 32),
            Leaf(key.Child(2), 1, 33),
            Leaf(key.Child(3), 2, 34)
        ], horizontalSampleLevel: 1);
        var outgoing = TerrainLodTileMessage.Of(-1, expected);
        using MemoryStream stream = new();
        outgoing.Write(stream);
        Assert.Equal(outgoing.Size(), stream.Length);
        stream.Position = 0;
        TerrainLodTileMessage incoming = new();
        incoming.Read(stream);

        var actual = incoming.Decode();

        Assert.Equal(-1, incoming.Dimension);
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.CanonicalHash, actual.CanonicalHash);
        Assert.Equal(expected.InputHashes, actual.InputHashes);
        Assert.Equal(expected[0, 0].Spans, actual[0, 0].Spans);
    }

    [Fact]
    public void Generated_maximum_level_round_trips_through_disk_and_transport()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var policy = TerrainLodSpatialPolicy.CreateDefault();
            var key = new TerrainLodTileKey(
                TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel, -2, 3);
            var column = TerrainLodColumn.Create(8,
            [
                new TerrainLodColumnSpan(
                    0, 8, Materials.Resolve(1, metadata: 0), blockLight: 0, skyLight: 15)
            ]);
            var expected = TerrainLodColumnTile.CreateUniform(
                key,
                policy.HorizontalSampleLevelForSpatialLevel(key.Level),
                8,
                column,
                "level-six-round-trip");
            var store = Store(root);

            Assert.Equal(TerrainLodColumnTileCacheWriteStatus.Written,
                store.Write(expected));
            var disk = Assert.IsType<TerrainLodColumnTile>(store.Read(key).Tile);
            var wire = TerrainLodTileMessage.Of(0, expected).Decode();

            Assert.Equal(64, expected.Width);
            Assert.Equal(expected.CanonicalHash, disk.CanonicalHash);
            Assert.Equal(expected.CanonicalHash, wire.CanonicalHash);
            Assert.Equal(key, wire.Key);

            var expandedStore = Store(
                root, TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel);
            var reused = expandedStore.Read(key);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Hit, reused.Status);
            Assert.Equal(expected.CanonicalHash, reused.Tile!.CanonicalHash);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Dormant_level_ten_parent_builds_and_round_trips_through_disk_cache()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var policy = TerrainLodSpatialPolicy.CreateForMaximumHorizon(
                TerrainLodSpatialPolicy.MaximumGeneratedHorizonChunks);
            var key = new TerrainLodTileKey(
                TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel, 1, -1);
            var column = TerrainLodColumn.Create(8,
            [
                new TerrainLodColumnSpan(
                    0, 8, Materials.Resolve(1, metadata: 0), blockLight: 0, skyLight: 15)
            ]);
            var children = Enumerable.Range(0, 4)
                .Select(index => TerrainLodColumnTile.CreateUniform(
                    key.Child(index),
                    policy.HorizontalSampleLevelForSpatialLevel(key.Level - 1),
                    8,
                    column,
                    $"level-ten-child-{index}"))
                .ToArray();
            var expected = TerrainLodColumnTile.BuildParent(
                key,
                children,
                policy.HorizontalSampleLevelForSpatialLevel(key.Level));
            var boundedStore = Store(root);
            var rejectedRead = boundedStore.Read(key);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Incompatible,
                rejectedRead.Status);
            Assert.Contains("exceeds cache manifest maximum 6", rejectedRead.Diagnostic);
            var writeError = Assert.Throws<InvalidOperationException>(() =>
                boundedStore.Write(expected));
            Assert.Contains("exceeds cache manifest maximum 6", writeError.Message);

            var store = Store(
                root, TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel);

            Assert.Equal(64, expected.Width);
            Assert.Equal(TerrainLodColumnTileCacheWriteStatus.Written,
                store.Write(expected));
            var actual = Assert.IsType<TerrainLodColumnTile>(store.Read(key).Tile);
            Assert.Equal(expected.CanonicalHash, actual.CanonicalHash);
            Assert.Equal(expected.InputHashes, actual.InputHashes);

            var wire = TerrainLodTileMessage.Of(0, expected);
            var transportError = Assert.Throws<InvalidDataException>(() => wire.Decode());
            Assert.Contains("exceeding the negotiated maximum 6", transportError.Message);
            Assert.Equal(expected.CanonicalHash,
                wire.Decode(TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel).CanonicalHash);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Transport_rejects_levels_outside_the_generated_policy()
    {
        TerrainLodTileRequestMessage request = new()
        {
            Keys = [new TerrainLodTileKey(
                TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel + 1, 0, 0)]
        };
        using MemoryStream stream = new();

        Assert.Throws<InvalidOperationException>(() => request.Write(stream));
    }

    [Fact]
    public void Leaf_tile_round_trips_revision_air_intervals_and_light()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var expected = LeafWithCave(new TerrainLodTileKey(0, -5, -7), revision: 42);
            var store = Store(root);
            store.Write(expected);

            var actual = Assert.IsType<TerrainLodColumnTile>(
                store.Read(expected.Key).Tile);

            Assert.Equal(42, actual.LeafTerrainRevision);
            Assert.Equal(expected.InputHashes, actual.InputHashes);
            Assert.Equal(3, actual[0, 0].Spans.Count);
            Assert.True(actual[0, 0].Spans[1].IsAir);
            Assert.Equal(9, actual[0, 0].Spans[2].BlockLight);
            Assert.Equal(13, actual[0, 0].Spans[2].SkyLight);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Leaf_cache_hit_requires_matching_revision_and_source_fingerprint()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var expected = LeafWithCave(new TerrainLodTileKey(0, 6, -8), revision: 42);
            var sourceFingerprint = expected.InputHashes[0][
                (expected.InputHashes[0].IndexOf(':') + 1)..];
            var store = Store(root);
            store.Write(expected);

            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Hit,
                store.Read(expected.Key, 42, sourceFingerprint).Status);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.StaleTerrain,
                store.Read(expected.Key, 43, sourceFingerprint).Status);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.StaleTerrain,
                store.Read(expected.Key, 42, "different-source").Status);
            Assert.Equal(2, store.Snapshot().StaleReads);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Parent_built_from_cached_children_matches_parent_built_from_fresh_children()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var key = new TerrainLodTileKey(1, -3, -2);
            TerrainLodColumnTile[] freshChildren =
            [
                Leaf(key.Child(0), 1, 21),
                Leaf(key.Child(1), 2, 22),
                LeafWithCave(key.Child(2), 23),
                Leaf(key.Child(3), 1, 24)
            ];
            var expected = TerrainLodColumnTile.BuildParent(key, freshChildren, 1);
            var store = Store(root);
            foreach (var child in freshChildren) store.Write(child);
            var cachedChildren = freshChildren
                .Select(child => Assert.IsType<TerrainLodColumnTile>(
                    store.Read(child.Key).Tile))
                .ToArray();

            var actual = TerrainLodColumnTile.BuildParent(key, cachedChildren, 1);

            Assert.Equal(expected.InputHashes, actual.InputHashes);
            Assert.Equal(expected.CanonicalHash, actual.CanonicalHash);
            Assert.Equal(expected.Width, actual.Width);
            for (var x = 0; x < actual.Width; x++)
            for (var z = 0; z < actual.Width; z++)
                Assert.Equal(expected[x, z].Spans, actual[x, z].Spans);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Interrupted_replacement_preserves_previous_tile_and_removes_temporary_file()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var key = new TerrainLodTileKey(0, 4, -9);
            var previous = Leaf(key, 1, 3);
            Store(root).Write(previous);
            var interrupted = new TerrainLodColumnTileCacheStore(
                root,
                Identity(),
                8 * 1024 * 1024,
                8 * 1024 * 1024,
                stage =>
                {
                    if (stage == TerrainLodCacheWriteStage.TemporaryDurable)
                        throw new IOException("simulated interruption");
                });

            Assert.Throws<IOException>(() => interrupted.Write(Leaf(key, 2, 4)));

            var reopened = Store(root);
            var actual = Assert.IsType<TerrainLodColumnTile>(reopened.Read(key).Tile);
            Assert.Equal(previous.CanonicalHash, actual.CanonicalHash);
            Assert.Empty(root.EnumerateFiles("*.tmp-*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Incompatible_and_corrupt_tiles_are_safe_cache_misses()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var key = new TerrainLodTileKey(0, -1, 2);
            var store = Store(root);
            store.Write(Leaf(key, 1, 5));
            var incompatible = new TerrainLodColumnTileCacheStore(
                root,
                Identity() with { ContentFingerprint = "different-content" },
                8 * 1024 * 1024,
                8 * 1024 * 1024);

            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Incompatible,
                incompatible.Read(key).Status);

            var file = Assert.Single(root.EnumerateFiles("*.ocol", SearchOption.AllDirectories));
            var bytes = File.ReadAllBytes(file.FullName);
            bytes[bytes.Length / 2] ^= 0x20;
            File.WriteAllBytes(file.FullName, bytes);

            var corrupt = store.Read(key);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Corrupt, corrupt.Status);
            Assert.Null(corrupt.Tile);
            Assert.Equal(1, store.Snapshot().CorruptReads);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Oversize_tile_is_rejected_without_replacing_previous_data()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var key = new TerrainLodTileKey(0, 7, 8);
            var previous = Leaf(key, 1, 1);
            var initial = Store(root);
            initial.Write(previous);
            var recordBytes = initial.Snapshot().CurrentBytes;
            var bounded = new TerrainLodColumnTileCacheStore(
                root,
                Identity(),
                recordBytes - 1,
                recordBytes - 1);

            Assert.Equal(TerrainLodColumnTileCacheWriteStatus.RejectedRecordTooLarge,
                bounded.Write(Leaf(key, 2, 2)));
            Assert.Equal(previous.CanonicalHash,
                Assert.IsType<TerrainLodColumnTile>(initial.Read(key).Tile).CanonicalHash);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Policy_v1_tile_is_a_nondestructive_miss_then_replaced_by_block_scale_v2()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var key = new TerrainLodTileKey(2, -1, 0);
            var column = TerrainLodColumn.Create(8,
                [new TerrainLodColumnSpan(0, 8, Materials.Resolve(1, 0), 0, 15)]);
            var previous = TerrainLodColumnTile.CreateUniform(key, 1, 8, column, "previous");
            var store = Store(root);
            store.Write(previous);
            var file = Assert.Single(root.EnumerateFiles("*.ocol", SearchOption.AllDirectories));
            var record = File.ReadAllBytes(file.FullName);
            // Construct a checksummed format-v2 / policy-v1 fixture. Production writers only
            // accept the current policy, so alter the identity field, not the decoder rules.
            using (var stream = new MemoryStream(record))
            using (var reader = new BinaryReader(stream))
            {
                reader.ReadUInt64(); // signature
                Assert.Equal(2, reader.ReadInt32()); // disk format, independent of quality policy
                reader.ReadBytes(reader.ReadInt32()); // world
                reader.ReadInt32(); // dimension
                reader.ReadBytes(reader.ReadInt32()); // content
                reader.ReadBytes(reader.ReadInt32()); // generator
                reader.ReadInt32(); // reduction schema
                reader.ReadBytes(reader.ReadInt32()); // materials
                reader.ReadInt32(); // maximum spatial level
                var offset = checked((int)stream.Position);
                Assert.Equal(2, reader.ReadInt32());
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(offset, 4), 1);
            }
            SHA256.HashData(record.AsSpan(0, record.Length - 32), record.AsSpan(record.Length - 32));
            File.WriteAllBytes(file.FullName, record);

            var reopened = Store(root);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Incompatible, reopened.Read(key).Status);
            Assert.Equal(record, File.ReadAllBytes(file.FullName)); // A miss never deletes the old bytes.
            var replacement = TerrainLodColumnTile.CreateUniform(key, 0, 8, column, "replacement");
            Assert.Equal(TerrainLodColumnTileCacheWriteStatus.Written, reopened.Write(replacement));
            var loaded = Store(root).Read(key);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Hit, loaded.Status);
            Assert.Equal(64, loaded.Tile!.Width);
            Assert.Equal(0, loaded.Tile.HorizontalSampleLevel);
            Assert.Equal(replacement.CanonicalHash, loaded.Tile.CanonicalHash);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static TerrainLodColumnTileCacheStore Store(
        DirectoryInfo root,
        int maximumSpatialLevel = TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel) =>
        new(root, Identity(maximumSpatialLevel), 8 * 1024 * 1024, 8 * 1024 * 1024);

    private static TerrainLodCacheIdentity Identity(
        int maximumSpatialLevel = TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel) => new(
        "column-world",
        0,
        "column-content",
        "column-generator",
        TerrainLodHierarchy.ReductionSchemaVersion,
        Materials.RulesFingerprint,
        maximumSpatialLevel,
        TerrainLodSpatialPolicy.CurrentQualityPolicyVersion);

    private static TerrainLodColumnTile Leaf(
        TerrainLodTileKey key,
        byte block,
        long revision)
    {
        if (key.Level != 0) throw new ArgumentException("Expected a leaf key.", nameof(key));
        return TerrainLodColumnTile.BuildLeaf(
            Snapshot(key, (_, _, _) => block, revision), Materials);
    }

    private static TerrainLodColumnTile LeafWithCave(
        TerrainLodTileKey key,
        long revision)
    {
        const int height = 8;
        var blocks = new byte[16 * height * 16];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < height; y++)
            blocks[(x * 16 + z) * height + y] =
                x == 0 && z == 0 && y is >= 2 and < 4 ? (byte)0 : (byte)1;
        var sky = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        var block = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        for (var y = 4; y < height; y++)
        {
            block.SetNibble(0, y, 0, 9);
            sky.SetNibble(0, y, 0, 13);
        }
        return TerrainLodColumnTile.BuildLeaf(new TerrainLodSourceSnapshot(
            key.X,
            key.Z,
            16,
            height,
            16,
            blocks,
            new byte[blocks.Length],
            revision,
            new TerrainLodLightingSnapshot(
                key.X,
                key.Z,
                revision,
                sky.Bytes,
                block.Bytes,
                hasSkyLight: true)), Materials);
    }

    private static TerrainLodSourceSnapshot Snapshot(
        TerrainLodTileKey key,
        Func<int, int, int, byte> block,
        long revision,
        int height = 8)
    {
        var blocks = new byte[16 * height * 16];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < height; y++)
            blocks[(x * 16 + z) * height + y] = block(x, y, z);
        return new TerrainLodSourceSnapshot(
            key.X, key.Z, 16, height, 16, blocks, new byte[blocks.Length], revision);
    }

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var directory = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-column-tile-{Guid.NewGuid():N}"));
        directory.Create();
        return directory;
    }
}
