using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialMeshBuilderTests
{
    [Fact]
    public void Uniform_tile_culls_internal_faces_and_emits_paired_light_streams()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var tile = Leaf(materials, 0, 0, 128,
            (_, y, _) => y < 32 ? stone : (byte)0);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);

        Assert.Equal(tile.CanonicalHash, mesh.CanonicalHash);
        Assert.Equal(640, mesh.SolidQuadCount);
        Assert.Equal(0, mesh.TranslucentQuadCount);
        Assert.All(mesh.Pages, page =>
        {
            Assert.Equal(0, page.Vertices.Length % 4);
            Assert.Equal(page.Vertices.Length, page.Lights.Length);
            Assert.Equal(page.TranslucentVertices.Length, page.TranslucentLights.Length);
            Assert.Equal(page.Vertices.Length / 4, page.SolidRanges.AvailableQuadCount);
            Assert.Equal(
                page.TranslucentVertices.Length / 4,
                page.TranslucentRanges.AvailableQuadCount);
            Assert.Equal(0, page.SolidRanges.UnassignedQuadCount);
            Assert.Equal(0, page.TranslucentRanges.UnassignedQuadCount);
        });
    }

    [Fact]
    public void Tile_body_can_omit_boundaries_owned_by_the_spatial_seam_plan()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var tile = Leaf(materials, 0, 0, 128,
            (_, y, _) => y < 32 ? stone : (byte)0);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false);

        Assert.False(mesh.IncludesExternalBoundaryFaces);
        Assert.Equal(512, mesh.SolidQuadCount);
        Assert.All(mesh.Pages, page =>
        {
            Assert.True(page.SolidRanges.West.IsEmpty);
            Assert.True(page.SolidRanges.East.IsEmpty);
            Assert.True(page.SolidRanges.North.IsEmpty);
            Assert.True(page.SolidRanges.South.IsEmpty);
        });
    }

    [Fact]
    public void Liquid_geometry_is_kept_in_the_translucent_stream()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var water = checked((byte)world.Content.Blocks.Get("omniblock:flowing_water").Id);
        var tile = Leaf(materials, 0, 0, 128,
            (_, y, _) => y < 8 ? water : (byte)0);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);

        Assert.Equal(0, mesh.SolidQuadCount);
        // The 16x16 liquid top and bottom are each one tiled quad. Only the 64 outer wall
        // segments remain separate; the former per-column top/bottom grid is gone.
        Assert.Equal(66, mesh.TranslucentQuadCount);
        Assert.All(mesh.Pages.SelectMany(static page => page.TranslucentLights), light =>
        {
            Assert.InRange(light.Block, (byte)0, (byte)60);
            Assert.InRange(light.Sky, (byte)0, (byte)60);
        });
    }

    [Fact]
    public void Stationary_and_flowing_water_do_not_create_internal_tile_walls()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var flowing = checked((byte)world.Content.Blocks.Get("omniblock:flowing_water").Id);
        var stationary = checked((byte)world.Content.Blocks.Get("omniblock:water").Id);
        var tile = Leaf(materials, 0, 0, 32,
            (x, y, _) => y < 8 ? x < 8 ? flowing : stationary : (byte)0);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);

        Assert.DoesNotContain(mesh.Pages, page => HasQuadOnWorldPlane(
            page, page.TranslucentVertices, axis: 0, position: 8));
    }

    [Fact]
    public void Vertical_budget_changes_only_the_presentation_copy()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var water = checked((byte)world.Content.Blocks.Get("omniblock:flowing_water").Id);
        var tile = Leaf(materials, 0, 0, 8,
            (_, y, _) => y switch
            {
                < 2 => stone,
                < 4 => 0,
                < 6 => water,
                _ => stone
            });
        var canonicalSpanCount = tile[0, 0].Spans.Count;

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 2);

        Assert.Equal(4, canonicalSpanCount);
        Assert.Equal(canonicalSpanCount, tile[0, 0].Spans.Count);
        Assert.InRange(mesh.MaximumRenderedSpans, 1, 2);
    }

    [Fact]
    public void Cave_culling_removes_underground_faces_without_mutating_canonical_spans()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var blocks = new byte[ChuckFormat.ChunkSize];
        var metadata = new byte[blocks.Length];
        var sky = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        var block = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.ChunkHeight; y++)
        {
            blocks[ChuckFormat.GetIndex(x, y, z)] =
                y < 16 || y is >= 24 and < 32 ? stone : (byte)0;
            if (y >= 32) sky.SetNibble(x, y, z, 15);
        }
        var source = new TerrainLodSourceSnapshot(
            0, 0, 16, ChuckFormat.ChunkHeight, 16,
            blocks, metadata, terrainRevision: 1,
            new TerrainLodLightingSnapshot(
                0, 0, 1, sky.Bytes, block.Bytes, hasSkyLight: true));
        var tile = TerrainLodColumnTile.BuildLeaf(source, materials);
        var canonicalSpans = tile[0, 0].Spans.ToArray();

        var unculled = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false);
        var culled = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false, caveCullBelowY: 32);

        Assert.True(culled.SolidQuadCount < unculled.SolidQuadCount);
        Assert.Equal(canonicalSpans, tile[0, 0].Spans);
    }

    [Fact]
    public void Exposed_top_faces_sample_sunlit_air_instead_of_opaque_block_light()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var blocks = new byte[ChuckFormat.ChunkSize];
        var metadata = new byte[blocks.Length];
        var sky = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        var block = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.ChunkHeight; y++)
        {
            blocks[ChuckFormat.GetIndex(x, y, z)] = y < 32 ? stone : (byte)0;
            if (y >= 32) sky.SetNibble(x, y, z, 15);
        }
        var tile = TerrainLodColumnTile.BuildLeaf(
            new TerrainLodSourceSnapshot(
                0, 0, 16, ChuckFormat.ChunkHeight, 16,
                blocks, metadata, terrainRevision: 1,
                new TerrainLodLightingSnapshot(
                    0, 0, 1, sky.Bytes, block.Bytes, hasSkyLight: true)),
            materials);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false, caveCullBelowY: 60);
        var topLights = mesh.Pages.SelectMany(page =>
        {
            var range = page.SolidRanges.Up;
            return page.Lights.Skip(range.FirstQuad * 4).Take(range.QuadCount * 4);
        }).ToArray();

        Assert.NotEmpty(topLights);
        Assert.All(topLights, light => Assert.Equal(60, light.Sky));
    }

    [Fact]
    public void Large_parent_is_partitioned_into_packed_vertex_safe_pages()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var root = BuildUniformTree(
            new TerrainLodTileKey(3, 0, 0), materials, stone, height: 8);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            root, world.Content.Blocks, verticalSliceBudget: 4);

        Assert.Equal(4, mesh.Pages.Length);
        Assert.Equal(4, mesh.Pages.Select(static page => (page.Key.X, page.Key.Z)).Distinct().Count());
        Assert.All(mesh.Pages, page =>
        {
            Assert.Equal(0, page.OriginX % TerrainLodSpatialMeshBuilder.PageSize);
            Assert.Equal(0, page.OriginY % TerrainLodSpatialMeshBuilder.PageSize);
            Assert.Equal(0, page.OriginZ % TerrainLodSpatialMeshBuilder.PageSize);
            Assert.All(page.Vertices, vertex =>
            {
                Assert.InRange(vertex.X, (short)0, short.MaxValue);
                Assert.InRange(vertex.Y, (short)0, short.MaxValue);
                Assert.InRange(vertex.Z, (short)0, short.MaxValue);
                Assert.InRange(vertex.U, (ushort)0, ushort.MaxValue);
                Assert.InRange(vertex.V, (ushort)0, ushort.MaxValue);
            });
        });
    }

    [Theory]
    [InlineData(5, 32, 1)]
    [InlineData(6, 64, 2)]
    public void Public_large_horizon_samples_use_packed_uv_scale_without_losing_tiling(
        int horizontalSampleLevel,
        int expectedSampleSize,
        byte expectedExponent)
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = world.Content.Blocks.Get("omniblock:stone");
        var column = TerrainLodColumn.Create(128,
        [
            new TerrainLodColumnSpan(
                0, 64, materials.Resolve(stone.Id, metadata: 0), blockLight: 0, skyLight: 0),
            new TerrainLodColumnSpan(
                64, 64, TerrainLodMaterial.Air, blockLight: 0, skyLight: 15)
        ]);
        var tile = TerrainLodColumnTile.CreateUniform(
            new TerrainLodTileKey(horizontalSampleLevel + 2, 0, 0),
            horizontalSampleLevel,
            128,
            column,
            $"packed-uv-{horizontalSampleLevel}");

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 4,
            emitTileBoundaryFaces: false);
        var vertices = mesh.Pages.SelectMany(static page => page.Vertices).ToArray();

        Assert.NotEmpty(vertices);
        Assert.All(vertices, vertex => Assert.Equal(expectedExponent, vertex.UvScaleExponent));
        Assert.Equal(expectedSampleSize,
            vertices.Max(vertex => vertex.U / 4095f * (1 << vertex.UvScaleExponent)),
            precision: 3);
        Assert.Equal(expectedSampleSize,
            vertices.Max(vertex => vertex.V / 4095f * (1 << vertex.UvScaleExponent)),
            precision: 3);
    }

    private static TerrainLodColumnTile BuildUniformTree(
        TerrainLodTileKey key,
        TerrainLodMaterialCatalog materials,
        byte block,
        int height)
    {
        if (key.Level == 0)
            return Leaf(materials, key.X, key.Z, height,
                (_, y, _) => y < height / 2 ? block : (byte)0);
        var children = Enumerable.Range(0, 4)
            .Select(index => BuildUniformTree(key.Child(index), materials, block, height))
            .ToArray();
        return TerrainLodColumnTile.BuildParent(
            key, children, horizontalSampleLevel: key.Level);
    }

    private static TerrainLodColumnTile Leaf(
        TerrainLodMaterialCatalog materials,
        int chunkX,
        int chunkZ,
        int height,
        Func<int, int, int, byte> block)
    {
        var blocks = new byte[checked(16 * height * 16)];
        var metadata = new byte[blocks.Length];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < height; y++)
            blocks[(x * 16 + z) * height + y] = block(x, y, z);
        var source = new TerrainLodSourceSnapshot(
            chunkX, chunkZ, 16, height, 16, blocks, metadata, terrainRevision: 1);
        return TerrainLodColumnTile.BuildLeaf(source, materials);
    }

    private static bool HasQuadOnWorldPlane(
        TerrainLodSpatialMeshPage page,
        ChunkVertex[] vertices,
        int axis,
        float position)
    {
        for (var index = 0; index < vertices.Length; index += 4)
        {
            var onPlane = true;
            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = vertices[index + corner];
                var coordinate = axis switch
                {
                    0 => page.OriginX + vertex.X * 64.0f / 32767.0f,
                    1 => page.OriginY + vertex.Y * 64.0f / 32767.0f,
                    _ => page.OriginZ + vertex.Z * 64.0f / 32767.0f
                };
                onPlane &= Math.Abs(coordinate - position) < 0.01f;
            }
            if (onPlane) return true;
        }
        return false;
    }
}
