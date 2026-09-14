using OmniBlock.Server.Worlds;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodHierarchyTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x707070),
        new TerrainLodMaterialDefinition(2, "example:water",
            TerrainLodGeometryClass.Liquid, false, 0x4040FF),
        new TerrainLodMaterialDefinition(3, "example:leaves",
            TerrainLodGeometryClass.Cutout, false, 0x007C00)
    ]);

    [Fact]
    public void Snapshot_copies_source_arrays_and_preserves_revision()
    {
        byte[] blocks = [1, 0];
        byte[] metadata = [3, 0];
        var snapshot = new TerrainLodSourceSnapshot(7, -9, 1, 2, 1,
            blocks, metadata, 42);

        blocks[0] = 0;
        metadata[0] = 0;

        Assert.Equal(1, snapshot.GetBlock(0, 0, 0));
        Assert.Equal(3, snapshot.GetMetadata(0, 0, 0));
        Assert.Equal(42, snapshot.TerrainRevision);
    }

    [Fact]
    public void Solid_volume_reduces_to_a_fully_occupied_stone_cell()
    {
        var hierarchy = Build(2, 2, 2, (_, _, _) => 1);

        Assert.Equal(2, hierarchy.Levels.Count);
        var root = hierarchy.Levels[1][0, 0, 0];
        Assert.Equal("example:stone", root.Primary.BlockId.ToString());
        Assert.Equal(8u, root.PrimaryCoverage);
        Assert.Equal(byte.MaxValue, root.OccupancyMask);
        Assert.Equal(TerrainLodFaceMask.All, root.ExposedFaces);
    }

    [Fact]
    public void Mixed_cell_keeps_octant_topology_and_external_silhouette()
    {
        var hierarchy = Build(2, 2, 2, (x, y, z) =>
            x == 0 && y == 0 && z == 0 ? (byte)1 : (byte)0);

        var root = hierarchy.Levels[1][0, 0, 0];
        Assert.Equal(0b0000_0001, root.OccupancyMask);
        Assert.Equal(1u, root.PrimaryCoverage);
        Assert.Equal(TerrainLodFaceMask.All, root.ExposedFaces);
    }

    [Fact]
    public void Surface_preserving_reduction_retains_thin_water_and_foliage_layers()
    {
        var source = Snapshot(2, 2, 2, (x, y, z) =>
            y == 1 && x == 0 ? (byte)2 : y == 1 && x == 1 && z == 0 ? (byte)3 : (byte)1);

        var volume = TerrainLodReducer.Build(
            source, Materials, TerrainLodReductionStrategy.VolumeDominant).Levels[1][0, 0, 0];
        var surface = TerrainLodReducer.Build(
            source, Materials, TerrainLodReductionStrategy.SurfacePreserving).Levels[1][0, 0, 0];

        Assert.Equal("example:stone", volume.Primary.BlockId.ToString());
        Assert.Equal("example:water", surface.Primary.BlockId.ToString());
        Assert.Contains("example:leaves", new[]
        {
            surface.Primary.BlockId.ToString(),
            surface.Secondary.BlockId.ToString(),
            surface.Tertiary.BlockId.ToString()
        });
    }

    [Fact]
    public void Enclosed_air_survives_as_a_mixed_parent_instead_of_becoming_solid()
    {
        var hierarchy = Build(4, 4, 4, (x, y, z) =>
            x == 1 && y == 1 && z == 1 ? (byte)0 : (byte)1);

        var affected = hierarchy.Levels[1][0, 0, 0];
        Assert.NotEqual(byte.MaxValue, affected.OccupancyMask);
        Assert.Equal(7u, affected.PrimaryCoverage);
    }

    [Fact]
    public void Fingerprint_is_deterministic_and_includes_strategy_and_revision()
    {
        var source = Snapshot(4, 4, 4, (x, y, z) =>
            (byte)((x + y + z) % 3 + 1), revision: 9);
        var first = TerrainLodReducer.Build(
            source, Materials, TerrainLodReductionStrategy.SurfacePreserving);
        var second = TerrainLodReducer.Build(
            source, Materials, TerrainLodReductionStrategy.SurfacePreserving);
        var otherStrategy = TerrainLodReducer.Build(
            source, Materials, TerrainLodReductionStrategy.VolumeDominant);
        var otherRevision = TerrainLodReducer.Build(
            Snapshot(4, 4, 4, (x, y, z) => (byte)((x + y + z) % 3 + 1), revision: 10),
            Materials,
            TerrainLodReductionStrategy.SurfacePreserving);

        Assert.Equal(first.CanonicalHash, second.CanonicalHash);
        Assert.NotEqual(first.CanonicalHash, otherStrategy.CanonicalHash);
        Assert.NotEqual(first.CanonicalHash, otherRevision.CanonicalHash);
    }

    [Fact]
    public void Unknown_protocol_id_fails_conversion_instead_of_baking_a_wrong_material()
    {
        var source = Snapshot(1, 1, 1, (_, _, _) => 99);

        var error = Assert.Throws<InvalidDataException>(() => TerrainLodReducer.Build(
            source, Materials, TerrainLodReductionStrategy.SurfacePreserving));

        Assert.Contains("protocol ID 99", error.Message);
    }

    [Fact]
    public void Live_and_serialized_terrain_produce_identical_lod_without_entity_materialization()
    {
        var world = new FakeWorldContext();
        var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], 3, -5);
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        {
            chunk[x, 0, z] = stone;
            if ((x + z) % 5 == 0)
            {
                chunk[x, 1, z] = water;
                chunk.Meta.SetNibble(x, 1, z, (x + z) & 7);
            }
        }

        var live = TerrainLodSourceSnapshot.Capture(chunk, 12);
        var serialized = InactiveChunkSnapshot.Capture(chunk, world).CaptureTerrain(12);
        var catalog = TerrainLodMaterialCatalog.FromRuntime(world.Content);

        var liveHierarchy = TerrainLodReducer.Build(
            live, catalog, TerrainLodReductionStrategy.SurfacePreserving);
        var importedHierarchy = TerrainLodReducer.Build(
            serialized, catalog, TerrainLodReductionStrategy.SurfacePreserving);

        Assert.Equal(liveHierarchy.CanonicalHash, importedHierarchy.CanonicalHash);
        Assert.Empty(chunk.Entities.SelectMany(static entities => entities));
        Assert.Empty(chunk.BlockEntities);
    }

    [Fact]
    public void Built_in_fallbacks_classify_terrain_without_client_graphics_state()
    {
        var world = new FakeWorldContext();
        var catalog = TerrainLodMaterialCatalog.FromRuntime(world.Content);

        Assert.Equal(TerrainLodGeometryClass.Opaque,
            Resolve("omniblock:stone").Geometry);
        Assert.Equal(TerrainLodGeometryClass.Liquid,
            Resolve("omniblock:flowing_water").Geometry);
        Assert.Equal(TerrainLodGeometryClass.Cutout,
            Resolve("omniblock:leaves").Geometry);
        Assert.Equal(TerrainLodGeometryClass.Translucent,
            Resolve("omniblock:glass").Geometry);
        Assert.Equal(TerrainLodGeometryClass.ConservativeCube,
            Resolve("omniblock:moving_piston").Geometry);

        TerrainLodMaterial Resolve(string key)
        {
            var block = world.Content.Blocks.Get(key);
            return catalog.Resolve(block.Id, 0);
        }
    }

    private static TerrainLodHierarchy Build(
        int width,
        int height,
        int depth,
        Func<int, int, int, byte> block) => TerrainLodReducer.Build(
        Snapshot(width, height, depth, block),
        Materials,
        TerrainLodReductionStrategy.SurfacePreserving);

    private static TerrainLodSourceSnapshot Snapshot(
        int width,
        int height,
        int depth,
        Func<int, int, int, byte> block,
        long revision = 0)
    {
        var blocks = new byte[width * height * depth];
        var metadata = new byte[blocks.Length];
        for (var x = 0; x < width; x++)
        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
            blocks[(x * depth + z) * height + y] = block(x, y, z);
        return new TerrainLodSourceSnapshot(0, 0, width, height, depth,
            blocks, metadata, revision);
    }
}
