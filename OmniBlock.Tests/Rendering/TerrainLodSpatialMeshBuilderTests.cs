using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialMeshBuilderTests
{
    [Fact]
    public void Published_source_probe_samples_world_coordinates_without_wrapping_at_negative_tile_edges()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var snow = checked((byte)world.Content.Blocks.Get("omniblock:snow").Id);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var tile = Build(new TerrainLodTileKey(2, -1, 1));

        Assert.Equal("omniblock:snow",
            TerrainLodQualityDiagnostics.Sample(tile, -13, 8, 68)?.Material.BlockId.ToString());
        Assert.Equal("omniblock:stone",
            TerrainLodQualityDiagnostics.Sample(tile, -12, 8, 68)?.Material.BlockId.ToString());
        Assert.Null(TerrainLodQualityDiagnostics.Sample(tile, 0, 8, 68));
        Assert.Null(TerrainLodQualityDiagnostics.Sample(tile, -13, 8, 128));
        Assert.Null(TerrainLodQualityDiagnostics.Sample(tile, -13, 16, 68));

        TerrainLodColumnTile Build(TerrainLodTileKey key)
        {
            if (key.Level == 0)
                return Leaf(materials, key.X, key.Z, 16,
                    (x, y, z) => y == 8 && key.X == -1 && key.Z == 4 && x == 3 && z == 4
                        ? snow : stone);
            return TerrainLodColumnTile.BuildParent(key,
                Enumerable.Range(0, 4).Select(index => Build(key.Child(index))).ToArray(),
                horizontalSampleLevel: 0);
        }
    }

    [Fact]
    public void Build_observes_cancellation_before_allocating_mesh_output()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var tile = Leaf(materials, 0, 0, 32,
            (_, y, _) => y < 8 ? stone : (byte)0);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            TerrainLodSpatialMeshBuilder.Build(
                tile, world.Content.Blocks, verticalSliceBudget: 8,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Build_stops_when_the_retained_result_cannot_fit_its_budget()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var tile = Leaf(materials, 0, 0, 32,
            (_, y, _) => y < 8 ? stone : (byte)0);

        var error = Assert.Throws<TerrainLodSpatialMeshBudgetExceededException>(() =>
            TerrainLodSpatialMeshBuilder.Build(
                tile, world.Content.Blocks, verticalSliceBudget: 8,
                maximumResultBytes: 1));

        Assert.Equal(1, error.MaximumResultBytes);
        Assert.True(error.AttemptedResultBytes > error.MaximumResultBytes);
    }

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
        Assert.Equal(tile.Width * tile.Width, mesh.Profile.SourceColumns);
        Assert.True(mesh.Profile.SourceSpans >= mesh.Profile.SourceColumns);
        Assert.True(mesh.Profile.ConstructionPages > 0);
        Assert.True(mesh.Profile.ReductionMs >= 0);
        Assert.True(mesh.Profile.FaceEmissionMs >= 0);
        Assert.True(mesh.Profile.FlatteningMs >= 0);
        Assert.True(mesh.Profile.CoalescingMs >= 0);
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
    public void Finest_spatial_samples_preserve_crossed_plants_as_two_sided_non_directional_quads()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var grass = checked((byte)world.Content.Blocks.Get("omniblock:grass").Id);
        var tile = BuildTile(new TerrainLodTileKey(2, 0, 0));

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false);

        Assert.Equal(4, mesh.SolidQuadCount);
        Assert.Equal(0, mesh.TranslucentQuadCount);
        var page = Assert.Single(mesh.Pages);
        Assert.Equal(4, page.SolidRanges.UnassignedQuadCount);
        Assert.Equal(16, page.Vertices.Length);
        Assert.Equal(page.Vertices.Length, page.Lights.Length);
        Assert.Equal(0, page.Vertices[0].U);
        Assert.Equal(4095, page.Vertices[2].U);
        Assert.True(page.SolidRanges.Down.IsEmpty && page.SolidRanges.Up.IsEmpty);
        Span<ChunkQuadRange> selected = stackalloc ChunkQuadRange[7];
        Assert.Equal(1, page.SolidRanges.Select(ChunkDirectionMask.None, selected));
        Assert.Equal(4, selected[0].QuadCount);

        TerrainLodColumnTile BuildTile(TerrainLodTileKey key)
        {
            if (key.Level == 0)
                return Leaf(materials, key.X, key.Z, 32,
                    (x, y, z) => key.X == 0 && key.Z == 0 &&
                        x == 3 && z == 4 && y == 8 ? grass : (byte)0);
            var children = Enumerable.Range(0, 4)
                .Select(index => BuildTile(key.Child(index))).ToArray();
            return TerrainLodColumnTile.BuildParent(
                key, children, horizontalSampleLevel: 0);
        }
    }

    [Fact]
    public void Coarser_spatial_samples_do_not_expand_crossed_plants_into_cubes_or_quads()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var grass = world.Content.Blocks.Get("omniblock:grass");
        var column = TerrainLodColumn.Create(32,
        [
            new TerrainLodColumnSpan(0, 8, TerrainLodMaterial.Air, 0, 15),
            new TerrainLodColumnSpan(8, 1, materials.Resolve(grass.Id, 0), 0, 15),
            new TerrainLodColumnSpan(9, 23, TerrainLodMaterial.Air, 0, 15)
        ]);
        var tile = TerrainLodColumnTile.CreateUniform(
            new TerrainLodTileKey(3, 0, 0), 1, 32, column, "coarse-crossed-sample");

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);

        Assert.Empty(mesh.Pages);
        Assert.Equal(0, mesh.SolidQuadCount);
    }

    [Fact]
    public void Finest_spatial_snow_uses_metadata_height_instead_of_a_full_cube()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var snow = world.Content.Blocks.Get("omniblock:snow");
        var column = TerrainLodColumn.Create(16,
        [
            new TerrainLodColumnSpan(0, 8, TerrainLodMaterial.Air, 0, 15),
            new TerrainLodColumnSpan(8, 1, materials.Resolve(snow.Id, 3), 0, 15),
            new TerrainLodColumnSpan(9, 7, TerrainLodMaterial.Air, 0, 15)
        ]);
        var tile = TerrainLodColumnTile.CreateUniform(
            new TerrainLodTileKey(1, 0, 0), 0, 16, column, "snow-height");

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);
        var top = mesh.Pages.SelectMany(page => page.Vertices.Select(vertex =>
            page.OriginY + vertex.Y * 64f / 32767f)).Max();

        Assert.InRange(top, 8.49f, 8.51f);
        Assert.True(mesh.SolidQuadCount > 0);
        Assert.DoesNotContain(mesh.Pages, page => HasQuadOnWorldPlane(
            page, page.Vertices, axis: 0, position: 1));
    }

    [Fact]
    public void Finest_spatial_cactus_uses_definition_bounds()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var cactus = world.Content.Blocks.Get("omniblock:cactus");
        var column = TerrainLodColumn.Create(16,
        [
            new TerrainLodColumnSpan(0, 8, TerrainLodMaterial.Air, 0, 15),
            new TerrainLodColumnSpan(8, 1, materials.Resolve(cactus.Id, 0), 0, 15),
            new TerrainLodColumnSpan(9, 7, TerrainLodMaterial.Air, 0, 15)
        ]);
        var tile = TerrainLodColumnTile.CreateUniform(
            new TerrainLodTileKey(1, 0, 0), 0, 16, column, "cactus-bounds");

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false);
        var xPositions = mesh.Pages.SelectMany(page => page.Vertices.Select(vertex =>
            page.OriginX + vertex.X * 64f / 32767f)).ToArray();

        Assert.InRange(xPositions.Min(), 0.0525f, 0.0725f);
        Assert.InRange(xPositions.Max(), 31.9275f, 31.9475f);
    }

    [Fact]
    public void Worldless_grass_side_overlay_uses_green_top_tint()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var grass = checked((byte)world.Content.Blocks.Get("omniblock:grass_block").Id);
        var tile = Leaf(materials, 0, 0, 16,
            (_, y, _) => y < 8 ? grass : (byte)0);
        var overlayLayer = OmniBlock.Textures.Atlases.Terrain.LayerOfGridIndex(
            OmniBlock.Textures.Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay"));

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);
        var overlay = mesh.Pages.SelectMany(page => page.Vertices)
            .Where(vertex => vertex.ArrayLayer == overlayLayer).ToArray();

        Assert.NotEmpty(overlay);
        Assert.Contains(overlay, vertex => vertex.Color ==
            TerrainLodMeshBuilder.PackTintedColor(0x79C05A, 0.6f));
        Assert.DoesNotContain(overlay, vertex => vertex.Color ==
            TerrainLodMeshBuilder.PackTintedColor(0xFFFFFF, 0.6f));
    }

    [Fact]
    public void Coarse_snow_samples_color_the_supporting_top_without_a_snow_cube()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = world.Content.Blocks.Get("omniblock:stone");
        var snow = world.Content.Blocks.Get("omniblock:snow");
        var column = TerrainLodColumn.Create(16,
        [
            new TerrainLodColumnSpan(0, 8, materials.Resolve(stone.Id, 0), 0, 15),
            new TerrainLodColumnSpan(8, 1, materials.Resolve(snow.Id, 3), 0, 15),
            new TerrainLodColumnSpan(9, 7, TerrainLodMaterial.Air, 0, 15)
        ]);
        var tile = TerrainLodColumnTile.CreateUniform(
            new TerrainLodTileKey(1, 0, 0), 1, 16, column, "coarse-snow-cap");
        var snowLayer = OmniBlock.Textures.Atlases.Terrain.LayerOfGridIndex(
            snow.GetTexture(OmniBlock.Blocks.Side.Up, 3));

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);

        Assert.Contains(mesh.Pages.SelectMany(page => page.Vertices), vertex =>
            vertex.ArrayLayer == snowLayer);
        Assert.DoesNotContain(mesh.Pages.SelectMany(page => page.Vertices), vertex =>
            vertex.ArrayLayer == snowLayer && vertex.Y > 8 * 32767f / 64f + 1);
    }

    [Fact]
    public void Grass_beneath_snow_uses_untinted_snowy_side_without_green_overlay()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var grass = world.Content.Blocks.Get("omniblock:grass_block");
        var snow = world.Content.Blocks.Get("omniblock:snow");
        var column = TerrainLodColumn.Create(16,
        [
            new TerrainLodColumnSpan(0, 8, materials.Resolve(grass.Id, 0), 0, 15),
            new TerrainLodColumnSpan(8, 1, materials.Resolve(snow.Id, 0), 0, 15),
            new TerrainLodColumnSpan(9, 7, TerrainLodMaterial.Air, 0, 15)
        ]);
        var tile = TerrainLodColumnTile.CreateUniform(
            new TerrainLodTileKey(1, 0, 0), 0, 16, column, "snowy-grass-side");
        var snowyLayer = OmniBlock.Textures.Atlases.Terrain.LayerOfGridIndex(
            OmniBlock.Textures.Atlases.Terrain.IndexOf("omniblock:grass_block_side_snowy"));
        var overlayLayer = OmniBlock.Textures.Atlases.Terrain.LayerOfGridIndex(
            OmniBlock.Textures.Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay"));

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);
        var vertices = mesh.Pages.SelectMany(page => page.Vertices).ToArray();

        Assert.Contains(vertices, vertex => vertex.ArrayLayer == snowyLayer);
        Assert.DoesNotContain(vertices, vertex => vertex.ArrayLayer == overlayLayer);
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
        Assert.Equal(256, mesh.Profile.VerticalReducedColumns);
        Assert.Equal(0, mesh.Profile.CaveCulledColumns);
        Assert.Equal(4 * 256, mesh.Profile.CanonicalSpans);
        Assert.Equal(mesh.Profile.CanonicalSpans, mesh.Profile.SourceSpans);
        Assert.True(mesh.Profile.RenderedSpans < mesh.Profile.SourceSpans);
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
        Assert.Equal(32, culled.CaveCullBelowY);
        Assert.Null(unculled.CaveCullBelowY);
        Assert.Equal(256, culled.Profile.CaveCulledColumns);
        Assert.Equal(0, unculled.Profile.CaveCulledColumns);
        Assert.Equal(0, culled.Profile.VerticalReducedColumns);
        Assert.True(culled.Profile.SourceSpans < culled.Profile.CanonicalSpans);
        Assert.Equal(culled.Profile.RenderedSpans, culled.Profile.SourceSpans);
        Assert.Equal(canonicalSpans, tile[0, 0].Spans);

        // Block-lit cave air is visible even without skylight. The presentation-only culler
        // must not erase it, and the canonical source remains the same in both cases.
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 16; y < 24; y++)
            block.SetNibble(x, y, z, 8);
        var litSource = new TerrainLodSourceSnapshot(
            0, 0, 16, ChuckFormat.ChunkHeight, 16,
            blocks, metadata, terrainRevision: 2,
            new TerrainLodLightingSnapshot(
                0, 0, 2, sky.Bytes, block.Bytes, hasSkyLight: true));
        var litTile = TerrainLodColumnTile.BuildLeaf(litSource, materials);
        var litUnculled = TerrainLodSpatialMeshBuilder.Build(
            litTile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false);
        var litCulled = TerrainLodSpatialMeshBuilder.Build(
            litTile, world.Content.Blocks, verticalSliceBudget: 8,
            emitTileBoundaryFaces: false, caveCullBelowY: 32);

        Assert.Equal(0, litCulled.Profile.CaveCulledColumns);
        Assert.Equal(litUnculled.SolidQuadCount, litCulled.SolidQuadCount);
        Assert.Equal(litTile.CanonicalHash, litCulled.CanonicalHash);
    }

    [Fact]
    public void Spatial_mesh_keeps_a_bounded_dark_cave_mouth()
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
            var shaft = x == 8 && z == 8 && y is >= 24 and < 32;
            blocks[ChuckFormat.GetIndex(x, y, z)] = y < 16 ||
                y is >= 24 and < 32 && !shaft ? stone : (byte)0;
            if (y >= 32 || shaft || (x == 8 && z == 8 && y is >= 16 and < 24))
                sky.SetNibble(x, y, z, 15);
        }
        var source = new TerrainLodSourceSnapshot(
            0, 0, 16, ChuckFormat.ChunkHeight, 16, blocks, metadata, 1,
            new TerrainLodLightingSnapshot(0, 0, 1, sky.Bytes, block.Bytes, true));
        var tile = TerrainLodColumnTile.BuildLeaf(source, materials);

        var unculled = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, 8, emitTileBoundaryFaces: false);
        var culled = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, 8, emitTileBoundaryFaces: false, caveCullBelowY: 32);

        Assert.InRange(culled.Profile.CaveCulledColumns, 1, 255);
        Assert.True(culled.SolidQuadCount < unculled.SolidQuadCount);
        Assert.True(TerrainLodCaveCuller.SealUndergroundAir(tile[11, 8], 32,
            TerrainLodCaveCuller.GetDefaultExposure(tile).ForColumn(11, 8)).At(20).IsAir);
        Assert.Equal(tile.CanonicalHash, culled.CanonicalHash);
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
    [InlineData(3, 8, 0)]
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
        var page = Assert.Single(mesh.Pages);
        var vertices = mesh.Pages.SelectMany(static page => page.Vertices).ToArray();

        Assert.NotEmpty(vertices);
        Assert.Equal(tile.Key.ChunkWidth * 16, page.ExtentX);
        Assert.Equal(tile.Key.ChunkWidth * 16, page.ExtentZ);
        Assert.Contains(vertices, static vertex =>
            vertex.PageOffsetXZ != 0 || vertex.PageOffsetY != 0);
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
