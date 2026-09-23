using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Registries;
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
    public void Detail_only_material_remains_available_for_coarse_surface_tint()
    {
        var materials = new TerrainLodMaterialCatalog(
        [
            new TerrainLodMaterialDefinition(1, "example:flower",
                TerrainLodGeometryClass.CrossedQuad, false, 0x00AA00, 1),
            new TerrainLodMaterialDefinition(2, "example:snow",
                TerrainLodGeometryClass.SurfaceLayer, false, 0xFFFFFF)
        ]);
        var flower = Snapshot(2, 2, 2, (_, _, _) => 1);
        var snow = Snapshot(2, 2, 2, (_, _, _) => 2);

        Assert.False(TerrainLodReducer.Build(flower, materials,
            TerrainLodReductionStrategy.SurfacePreserving).Levels[0][0, 0, 0].IsEmpty);
        Assert.False(TerrainLodReducer.Build(flower, materials,
            TerrainLodReductionStrategy.SurfacePreserving).Levels[1][0, 0, 0].IsEmpty);
        Assert.False(TerrainLodReducer.Build(snow, materials,
            TerrainLodReductionStrategy.SurfacePreserving).Levels[1][0, 0, 0].IsEmpty);
    }

    [Fact]
    public void Material_rules_fingerprint_includes_maximum_sample_size()
    {
        TerrainLodMaterialCatalog Catalog(int maxSampleSize) => new(
        [
            new TerrainLodMaterialDefinition(1, "example:flower",
                TerrainLodGeometryClass.CrossedQuad, false, 0x00AA00, maxSampleSize)
        ]);

        Assert.NotEqual(Catalog(1).RulesFingerprint, Catalog(2).RulesFingerprint);
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
        Assert.NotNull(live.Lighting);
        Assert.NotNull(serialized.Lighting);
        Assert.Equal(
            live.Lighting.GetLightLevels(3 * 16 + 4, 1, -5 * 16 + 7, 0),
            serialized.Lighting.GetLightLevels(3 * 16 + 4, 1, -5 * 16 + 7, 0));
        Assert.Empty(chunk.Entities.SelectMany(static entities => entities));
        Assert.Empty(chunk.BlockEntities);
    }

    [Fact]
    public void Local_lod_capture_rejects_partial_chunk_payloads()
    {
        var world = new FakeWorldContext();
        var chunk = new Chunk(world, 7, -4)
        {
            Blocks = new byte[ChuckFormat.ChunkSize],
            Meta = new ChunkNibbleArray(ChuckFormat.ChunkSize),
            BlockLight = new ChunkNibbleArray(ChuckFormat.ChunkSize),
            SkyLight = new ChunkNibbleArray(ChuckFormat.ChunkSize)
        };
        var partial = new byte[16 * 16 * 16 * 5 / 2];

        chunk.LoadFromPacket(partial, 0, 0, 0, 16, 16, 16, 0);

        Assert.True(chunk.Loaded);
        Assert.False(chunk.HasCompleteTerrainSnapshot);
        var error = Assert.Throws<InvalidOperationException>(() =>
            TerrainLodSourceSnapshot.Capture(chunk));
        Assert.Contains("7,-4", error.Message);

        var complete = new byte[ChuckFormat.ChunkSize * 5 / 2];
        chunk.LoadFromPacket(
            complete, 0, 0, 0, 16, ChuckFormat.ChunkHeight, 16, 0);

        Assert.True(chunk.HasCompleteTerrainSnapshot);
        Assert.NotNull(TerrainLodSourceSnapshot.Capture(chunk));
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
        Assert.Equal(TerrainLodGeometryClass.CrossedQuad,
            Resolve("omniblock:grass").Geometry);
        Assert.Equal(1, Resolve("omniblock:grass").MaxSampleSize);
        Assert.Equal(TerrainLodGeometryClass.CrossedQuad,
            Resolve("omniblock:wheat").Geometry);
        Assert.Equal(TerrainLodGeometryClass.SurfaceLayer,
            Resolve("omniblock:snow").Geometry);
        Assert.True(Resolve("omniblock:snow").MaxSampleSize > 2);
        Assert.Equal(1, Resolve("omniblock:cactus").MaxSampleSize);
        Assert.Equal(TerrainLodGeometryClass.BoundedCube,
            Resolve("omniblock:slab").Geometry);

        TerrainLodMaterial Resolve(string key)
        {
            var block = world.Content.Blocks.Get(key);
            return catalog.Resolve(block.Id, 0);
        }
    }

    [Fact]
    public void Explicit_block_descriptor_overrides_render_type_inference()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        var definition = JsonSerializer.Deserialize<BlockDefinition>(
            """
            {
              "ProtocolId": 240,
              "Material": "stone",
              "TerrainLod": {
                "Geometry": "CrossedQuad",
                "OccludesFaces": false
              }
            }
            """)!;
        definition.Name = "modded_billboard";
        definition.Namespace = Namespace.Get("example");
        builder.AddBlockDefinition(definition);

        var runtime = builder.Build();
        var block = runtime.Blocks.Get("example:modded_billboard");
        var material = TerrainLodMaterialCatalog.FromRuntime(runtime).Resolve(block.Id, 0);

        Assert.True(block.IsFrozen);
        Assert.Equal(
            new BlockTerrainLodDescriptor(TerrainLodGeometryClass.CrossedQuad, false, 1),
            block.TerrainLod);
        Assert.Equal(TerrainLodGeometryClass.CrossedQuad, material.Geometry);
        Assert.False(material.OccludesFaces);
    }

    [Fact]
    public void Empty_block_descriptor_uses_conservative_cube_fallback()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddBlockDefinition(new BlockDefinition
        {
            Name = "unknown_renderer",
            Namespace = Namespace.Get("example"),
            ProtocolId = 240,
            Material = "stone",
            NonOpaque = true,
            TerrainLod = new BlockTerrainLodDefinition()
        });

        var runtime = builder.Build();
        var block = runtime.Blocks.Get("example:unknown_renderer");
        var material = TerrainLodMaterialCatalog.FromRuntime(runtime).Resolve(block.Id, 0);

        Assert.Equal(TerrainLodGeometryClass.ConservativeCube, material.Geometry);
        Assert.True(material.OccludesFaces);
    }

    [Theory]
    [InlineData("Air", true, "cannot use the terrain LOD air geometry")]
    [InlineData("CrossedQuad", true, "cannot conservatively occlude")]
    [InlineData("NotAGeometry", false, "Unknown terrain LOD geometry")]
    public void Invalid_block_descriptor_fails_catalog_construction_with_owner(
        string geometry,
        bool occludesFaces,
        string expected)
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddBlockDefinition(new BlockDefinition
        {
            Name = "bad_lod",
            Namespace = Namespace.Get("example"),
            ProtocolId = 240,
            Material = "stone",
            TerrainLod = new BlockTerrainLodDefinition
            {
                Geometry = geometry,
                OccludesFaces = occludesFaces
            }
        });

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Block 'example:bad_lod' failed construction", error.Message);
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(128)]
    public void Invalid_maximum_sample_size_names_the_owning_block(int maxSampleSize)
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddBlockDefinition(new BlockDefinition
        {
            Name = "bad_lod_size",
            Namespace = Namespace.Get("example"),
            ProtocolId = 240,
            Material = "stone",
            TerrainLod = new BlockTerrainLodDefinition { MaxSampleSize = maxSampleSize }
        });

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("example:bad_lod_size", error.Message);
        Assert.Contains("MaxSampleSize", error.ToString());
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
