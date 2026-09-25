using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodTransientTileBuilderTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x777777)
    ]);

    [Fact]
    public void Transient_build_matches_normal_hierarchy_without_retaining_leaves()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var root = new TerrainLodTileKey(3, -1, 2);
        var builder = new TerrainLodTransientTileBuilder(root, policy, Materials);
        Dictionary<(int X, int Z), TerrainLodSourceSnapshot> sources = [];
        for (var x = (int)root.MinChunkX; x <= root.MaxChunkX; x++)
        for (var z = (int)root.MinChunkZ; z <= root.MaxChunkZ; z++)
            sources[(x, z)] = Source(x, z);

        while (!builder.IsComplete)
        {
            var coordinates = builder.NextChunkCoordinates();
            builder.AddSource(sources[coordinates]);
            Assert.InRange(builder.RetainedTiles, 0, root.Level * 3);
        }

        var expected = BuildReference(root);
        Assert.Equal(root.ChunkWidth * root.ChunkWidth, builder.SourcesAccepted);
        Assert.Equal(0, builder.RetainedTiles);
        Assert.Equal(expected.CanonicalHash, builder.Result!.CanonicalHash);
        Assert.Equal(expected.Width, builder.Result.Width);
        Assert.Equal(expected.HorizontalSampleLevel, builder.Result.HorizontalSampleLevel);

        TerrainLodColumnTile BuildReference(TerrainLodTileKey key)
        {
            if (key.Level == 0)
                return TerrainLodColumnTile.BuildLeaf(sources[(key.X, key.Z)], Materials);
            var children = Enumerable.Range(0, 4)
                .Select(index => BuildReference(key.Child(index)))
                .ToArray();
            return TerrainLodColumnTile.BuildParent(key, children,
                policy.HorizontalSampleLevelForSpatialLevel(key.Level));
        }
    }

    [Fact]
    public void Transient_build_rejects_out_of_order_or_wrong_chunk_sources()
    {
        var builder = new TerrainLodTransientTileBuilder(
            new TerrainLodTileKey(2, 1, -1), TerrainLodSpatialPolicy.CreateDefault(), Materials);
        var expected = builder.NextChunkCoordinates();
        Assert.Throws<ArgumentException>(() => builder.AddSource(Source(expected.X + 1, expected.Z)));
        Assert.Equal(0, builder.SourcesAccepted);
    }

    private static TerrainLodSourceSnapshot Source(int chunkX, int chunkZ)
    {
        const int height = 8;
        var blocks = new byte[16 * height * 16];
        var metadata = new byte[blocks.Length];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < height; y++)
        {
            var surface = 2 + ((chunkX * 7 + chunkZ * 11 + x + z) & 3);
            if (y < surface) blocks[(x * 16 + z) * height + y] = 1;
        }
        return new TerrainLodSourceSnapshot(
            chunkX, chunkZ, 16, height, 16, blocks, metadata);
    }
}
