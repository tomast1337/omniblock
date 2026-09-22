using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodMeshBuilderTests
{
    [Fact]
    public void Bounded_compiler_builds_cpu_levels_off_thread_from_an_immutable_snapshot()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var hierarchy = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);
        var chunk = world.ChunkHost.GetChunk(0, 0);
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < 32; y++)
            chunk.Blocks[ChuckFormat.GetIndex(x, y, z)] = (byte)stone;

        var visuals = new WorldRegionSnapshot(
            world, 0, 0, 0, 15, ChuckFormat.WorldHeight - 1, 15);
        var conversion = new TerrainLodConversionResult(
            0, 0, 0, 7, hierarchy);
        using var compiler = new TerrainLodMeshCompilationService(1);

        Assert.True(compiler.TrySubmit(new TerrainLodMeshCompilationRequest(
            conversion, 2, 4, visuals, true)));
        Assert.False(compiler.HasCapacity);
        TerrainLodMeshCompilationResult? result = null;
        Assert.True(SpinWait.SpinUntil(
            () => compiler.TryTakeCompleted(out result), TimeSpan.FromSeconds(5)));
        Assert.True(compiler.HasCapacity);

        Assert.NotNull(result);
        Assert.Null(result.Failure);
        Assert.NotNull(result.Boundaries);
        Assert.Equal(TerrainLodMeshWorkKind.Coverage, result.WorkKind);
        Assert.True(result.CompilationMs > 0);
        Assert.True(result.WorkCells > 0);
        Assert.True(result.RetainedBytes >= result.UploadBytes);
        Assert.True(result.UploadBytes > 0);
        Assert.Equal([2, 3, 4], result.Levels.Select(static level => level.Level));
        Assert.All(result.Levels, static level => Assert.NotEmpty(level.Vertices));
        var compilation = compiler.Snapshot();
        Assert.Equal(0, compilation.Owned);
        Assert.Equal(0, compilation.CompletedResultBytes);
        Assert.Equal(1, compilation.Cost.CompilationSamples);
    }

    [Fact]
    public void Refinement_cannot_consume_the_compilers_reserved_coverage_slot()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var hierarchy = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);
        var conversion = new TerrainLodConversionResult(0, 0, 0, 7, hierarchy);
        using var compiler = new TerrainLodMeshCompilationService(4);

        for (var i = 0; i < 3; i++)
            Assert.True(compiler.TrySubmit(Request(TerrainLodMeshWorkKind.Refinement, 0)));
        var rejected = Request(TerrainLodMeshWorkKind.Refinement, 0);
        Assert.False(compiler.TrySubmit(rejected));
        rejected.Visuals.Dispose();
        Assert.True(compiler.TrySubmit(Request(TerrainLodMeshWorkKind.Coverage, 2)));

        var snapshot = compiler.Snapshot();
        Assert.Equal(4, snapshot.Owned);
        Assert.True(snapshot.AdmissionDeferrals > 0);

        TerrainLodMeshCompilationRequest Request(TerrainLodMeshWorkKind kind, int minimum) => new(
            conversion,
            minimum,
            4,
            new WorldRegionSnapshot(
                world, 0, 0, 0, 15, ChuckFormat.WorldHeight - 1, 15),
            true,
            kind);
    }

    [Fact]
    public void Opaque_hierarchy_compiles_to_matching_quad_and_light_streams()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var hierarchy = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 2, world.Content.Blocks, true);

        Assert.NotEmpty(mesh.Vertices);
        Assert.Equal(128, mesh.Vertices.Length);
        Assert.Equal(0, mesh.Vertices.Length % 4);
        Assert.Equal(mesh.Vertices.Length, mesh.Lights.Length);
        Assert.All(mesh.Lights, light => Assert.True(light.Sky > 0));
        Assert.Empty(mesh.TranslucentVertices);
        Assert.Empty(mesh.TranslucentLights);
    }

    [Fact]
    public void Transition_level_preserves_two_block_surface_detail()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var hierarchy = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);

        var transition = TerrainLodMeshBuilder.Build(hierarchy, 1, world.Content.Blocks, true);
        var horizon = TerrainLodMeshBuilder.Build(hierarchy, 2, world.Content.Blocks, true);

        Assert.Equal(2, hierarchy.Levels[1].Scale);
        Assert.Equal(512, transition.Vertices.Length);
        Assert.True(transition.Vertices.Length > horizon.Vertices.Length);
        Assert.Equal(transition.Vertices.Length, transition.Lights.Length);
    }

    [Fact]
    public void Exact_voxel_level_preserves_one_cell_per_block()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var hierarchy = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);

        var exactVoxel = TerrainLodMeshBuilder.Build(hierarchy, 0, world.Content.Blocks, true);
        var transition = TerrainLodMeshBuilder.Build(hierarchy, 1, world.Content.Blocks, true);

        Assert.Equal(1, hierarchy.Levels[0].Scale);
        Assert.Equal(2048, exactVoxel.Vertices.Length);
        Assert.True(exactVoxel.Vertices.Length > transition.Vertices.Length);
        Assert.Equal(exactVoxel.Vertices.Length, exactVoxel.Lights.Length);
    }

    [Fact]
    public void Exact_voxel_plants_compile_to_two_double_sided_crossed_quads()
    {
        var world = new FakeWorldContext();
        var grass = world.Content.Blocks.Get("omniblock:grass").Id;
        var hierarchy = Build(world, (x, y, z) =>
            x == 8 && y == 32 && z == 8 ? (byte)grass : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 0, world.Content.Blocks, true);

        Assert.Equal(16, mesh.Vertices.Length);
        Assert.Equal(mesh.Vertices.Length, mesh.Lights.Length);
        Assert.Empty(mesh.TranslucentVertices);
        for (var offset = 0; offset < mesh.Vertices.Length; offset += 4)
        {
            var quad = mesh.Vertices.AsSpan(offset, 4);
            Assert.True(quad.ToArray().Select(static vertex => vertex.X).Distinct().Count() > 1);
            Assert.True(quad.ToArray().Select(static vertex => vertex.Z).Distinct().Count() > 1);
        }
    }

    [Fact]
    public void Coarse_plants_tint_the_supporting_surface_without_becoming_solid_cells()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var grass = world.Content.Blocks.Get("omniblock:grass").Id;
        var hierarchy = Build(world, (x, y, z) =>
            x != 8 || z != 8 ? (byte)0 : y == 1 ? (byte)stone : y == 2 ? (byte)grass : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 1, world.Content.Blocks, true);
        var expectedSample = TerrainLodMeshBuilder.PackTintedColor(
            TerrainLodMeshBuilder.BlendTint(0xFFFFFF, 0x007C00, 0.35f), 1);

        Assert.NotEmpty(mesh.Vertices);
        Assert.Contains(mesh.Vertices, vertex => vertex.Color == expectedSample);
        Assert.All(mesh.Vertices, vertex =>
            Assert.True(WorldY(vertex) <= 2.01f,
                $"crossed plant inflated coarse geometry to y={WorldY(vertex)}"));
    }

    [Fact]
    public void Exact_voxel_snow_uses_its_metadata_height_instead_of_a_full_cube()
    {
        var world = new FakeWorldContext();
        var snow = world.Content.Blocks.Get("omniblock:snow").Id;
        world.ReaderWriter.SetBlock(8, 32, 8, snow, 3);
        var hierarchy = Build(
            world,
            (x, y, z) => x == 8 && y == 32 && z == 8 ? (byte)snow : (byte)0,
            (x, y, z) => x == 8 && y == 32 && z == 8 ? (byte)3 : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(
            hierarchy, 0, world.Content.Blocks, true, visuals: world.Reader);

        Assert.NotEmpty(mesh.Vertices);
        Assert.InRange(mesh.Vertices.Max(WorldY), 32.49f, 32.51f);
    }

    [Fact]
    public void Coarse_snow_samples_its_surface_without_inflating_a_snow_cell()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var snowBlock = world.Content.Blocks.Get("omniblock:snow");
        var hierarchy = Build(world, (x, y, z) =>
            x != 8 || z != 8 ? (byte)0 : y == 1 ? (byte)stone : y == 2 ? (byte)snowBlock.Id : (byte)0);
        var snowLayer = OmniBlock.Textures.Atlases.Terrain.LayerOfGridIndex(
            snowBlock.GetTexture(OmniBlock.Blocks.Side.Up, 0));

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 1, world.Content.Blocks, true);

        Assert.Contains(mesh.Vertices, vertex => vertex.ArrayLayer == snowLayer);
        Assert.All(mesh.Vertices, vertex => Assert.True(WorldY(vertex) <= 2.01f));
    }

    [Fact]
    public void Liquid_only_hierarchy_uses_only_the_translucent_slice()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var hierarchy = Build(world, (x, y, z) => y < 8 ? (byte)water : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 2, world.Content.Blocks, true);

        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Lights);
        Assert.NotEmpty(mesh.TranslucentVertices);
        Assert.Equal(mesh.TranslucentVertices.Length, mesh.TranslucentLights.Length);

        var highestWorldY = mesh.TranslucentVertices.Max(vertex =>
            vertex.Y * 64.0f / 32767.0f + ChuckFormat.WorldHeight / 2.0f);
        Assert.InRange(highestWorldY, 7.8f, 7.95f);
    }

    [Fact]
    public void Solid_and_liquid_materials_compile_into_independent_layers()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var water = world.Content.Blocks.Get("omniblock:water").Id;
        var hierarchy = Build(world, (x, y, z) =>
            y < 12 ? (byte)stone : y < 16 ? (byte)water : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 2, world.Content.Blocks, true);

        Assert.NotEmpty(mesh.Vertices);
        Assert.NotEmpty(mesh.TranslucentVertices);
        Assert.Equal(0, mesh.Vertices.Length % 4);
        Assert.Equal(0, mesh.TranslucentVertices.Length % 4);
    }

    [Fact]
    public void Solid_column_mesh_does_not_own_its_outer_boundary_wall()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var terrain = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);
        var air = Build(world, (x, y, z) => 0);
        var airBoundary = TerrainLodBoundarySummary.Capture(air, 2, 4);

        var withoutEvidence = TerrainLodMeshBuilder.Build(
            terrain, 2, world.Content.Blocks, true);
        Assert.True(airBoundary.HasLevel(2));
        Assert.False(HasQuadOnPlane(withoutEvidence.Vertices, axis: 0, position: 16));
    }

    [Fact]
    public void Full_detail_boundary_summary_remains_surface_bounded()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var terrain = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);

        var boundary = TerrainLodBoundarySummary.Capture(terrain, 0, 4);

        Assert.InRange(boundary.EstimatedBytes, 1, 64 * 1024);
        Assert.Equal(new TerrainLodBoundaryIdentity(7, 0), boundary.Identity);
    }

    [Fact]
    public void Resident_opaque_neighbor_suppresses_the_shared_boundary()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var terrain = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);
        var opaqueBoundary = TerrainLodBoundarySummary.Capture(terrain, 2, 4);

        var withoutEvidence = TerrainLodMeshBuilder.Build(
            terrain, 2, world.Content.Blocks, true);
        Assert.True(opaqueBoundary.HasLevel(2));
        Assert.NotEmpty(withoutEvidence.Vertices);
    }

    [Fact]
    public void Fine_column_leaves_mixed_level_boundary_to_the_seam_artifact()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var fineTerrain = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);
        var air = Build(world, (x, y, z) => 0);
        var coarseAirBoundary = TerrainLodBoundarySummary.Capture(air, 2, 4);

        var withoutEvidence = TerrainLodMeshBuilder.Build(
            fineTerrain, 1, world.Content.Blocks, true);
        Assert.True(coarseAirBoundary.HasLevel(2));
        Assert.False(HasQuadOnPlane(withoutEvidence.Vertices, axis: 0, position: 16));
    }

    [Fact]
    public void Mixed_level_seam_emits_fine_patches_without_rebuilding_either_column()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var west = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0,
            chunkX: -3, chunkZ: 5);
        var east = Build(world, (x, y, z) => 0, chunkX: -2, chunkZ: 5);
        var westBoundary = TerrainLodBoundarySummary.Capture(west, 1, 4);
        var eastBoundary = TerrainLodBoundarySummary.Capture(east, 2, 4);

        var seam = TerrainLodSeamMeshBuilder.BuildSolid(
            westBoundary, 1, eastBoundary, 2, OmniBlock.Blocks.Side.East,
            world.Content.Blocks, true);

        // 8 cells along the edge x 16 vertical cells x one quad x four vertices.
        Assert.Equal(512, seam.Vertices.Length);
        Assert.Equal(seam.Vertices.Length, seam.Lights.Length);
        Assert.All(seam.Vertices, vertex =>
            Assert.InRange(vertex.X * 64.0f / 32767.0f, 15.99f, 16.01f));
        Assert.All(seam.Lights, light => Assert.Equal(60, light.Sky));
    }

    [Fact]
    public void Cave_culling_removes_unlit_underground_column_faces_but_keeps_the_surface()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var terrain = Build(world, (_, y, _) =>
            y < 16 || y is >= 24 and < 32 ? (byte)stone : (byte)0);
        var lighting = new HeightLight(32);

        var unculled = TerrainLodMeshBuilder.Build(
            terrain, 0, world.Content.Blocks, true, lighting);
        var culled = TerrainLodMeshBuilder.Build(
            terrain, 0, world.Content.Blocks, true, lighting,
            caveCullBelowY: 60);

        Assert.NotEmpty(culled.Vertices);
        Assert.True(culled.Vertices.Length < unculled.Vertices.Length);
        Assert.Contains(culled.Vertices, vertex => WorldY(vertex) >= 31.99f);
    }

    [Fact]
    public void Cave_culling_removes_unlit_underground_chunk_seams()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var west = Build(world, (_, y, _) => y < 16 ? (byte)stone : (byte)0,
            chunkX: 0, chunkZ: 0);
        var east = Build(world, (_, _, _) => 0, chunkX: 1, chunkZ: 0);
        var owner = TerrainLodBoundarySummary.Capture(west, 0, 4);
        var neighbor = TerrainLodBoundarySummary.Capture(east, 0, 4);

        var unculled = TerrainLodSeamMeshBuilder.BuildSolid(
            owner, 0, neighbor, 0, OmniBlock.Blocks.Side.East,
            world.Content.Blocks, true, lighting: new ConstantLight(0, 0));
        var culled = TerrainLodSeamMeshBuilder.BuildSolid(
            owner, 0, neighbor, 0, OmniBlock.Blocks.Side.East,
            world.Content.Blocks, true, lighting: new ConstantLight(0, 0),
            caveCullBelowY: 60);

        Assert.NotEmpty(unculled.Vertices);
        Assert.Empty(culled.Vertices);
    }

    [Fact]
    public void Shared_opaque_boundary_has_no_double_surface()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var west = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0,
            chunkX: 8, chunkZ: -11);
        var east = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0,
            chunkX: 9, chunkZ: -11);

        var seam = TerrainLodSeamMeshBuilder.BuildSolid(
            TerrainLodBoundarySummary.Capture(west, 1, 4), 1,
            TerrainLodBoundarySummary.Capture(east, 2, 4), 2,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true);

        Assert.Empty(seam.Vertices);
        Assert.Empty(seam.Lights);
    }

    [Fact]
    public void Seam_result_is_independent_of_which_side_contains_the_visible_material()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var solidWest = Build(world, (x, y, z) => y < 16 ? (byte)stone : (byte)0,
            chunkX: 0, chunkZ: 0);
        var airEast = Build(world, (x, y, z) => 0, chunkX: 1, chunkZ: 0);
        var airWest = Build(world, (x, y, z) => 0, chunkX: 0, chunkZ: 0);
        var solidEast = Build(world, (x, y, z) => y < 16 ? (byte)stone : (byte)0,
            chunkX: 1, chunkZ: 0);

        var facingEast = TerrainLodSeamMeshBuilder.BuildSolid(
            TerrainLodBoundarySummary.Capture(solidWest, 1, 4), 1,
            TerrainLodBoundarySummary.Capture(airEast, 2, 4), 2,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true);
        var facingWest = TerrainLodSeamMeshBuilder.BuildSolid(
            TerrainLodBoundarySummary.Capture(airWest, 1, 4), 1,
            TerrainLodBoundarySummary.Capture(solidEast, 2, 4), 2,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true);

        Assert.Equal(facingEast.Vertices.Length, facingWest.Vertices.Length);
        Assert.Equal(256, facingEast.Vertices.Length);
        Assert.True(FaceNormalX(facingEast.Vertices) > 0);
        Assert.True(FaceNormalX(facingWest.Vertices) < 0);
    }

    [Fact]
    public void Exact_to_lod_boundary_emits_only_the_lod_owned_solid_surface()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var exactAir = Build(world, (x, y, z) => 0, chunkX: -9, chunkZ: -4);
        var lodStone = Build(world, (x, y, z) => y < 16 ? (byte)stone : (byte)0,
            chunkX: -8, chunkZ: -4);
        var exact = TerrainLodBoundarySummary.Capture(exactAir, 0, 4);
        var lod = TerrainLodBoundarySummary.Capture(lodStone, 0, 4);

        var lodOwned = TerrainLodSeamMeshBuilder.BuildSolid(
            exact, 0, lod, 0, OmniBlock.Blocks.Side.East,
            world.Content.Blocks, true,
            lighting: new ConstantLight(9, 6),
            materialSide: TerrainLodSeamMaterialSide.Neighbor);
        var exactOwned = TerrainLodSeamMeshBuilder.BuildSolid(
            exact, 0, lod, 0, OmniBlock.Blocks.Side.East,
            world.Content.Blocks, true,
            materialSide: TerrainLodSeamMaterialSide.Owner);

        Assert.Equal(16 * 16 * 4, lodOwned.Vertices.Length);
        Assert.All(lodOwned.Lights, light =>
        {
            Assert.Equal(36, light.Sky);
            Assert.Equal(24, light.Block);
        });
        Assert.Empty(exactOwned.Vertices);
        Assert.True(FaceNormalX(lodOwned.Vertices) < 0);
        Assert.All(lodOwned.Vertices, vertex =>
            Assert.InRange(vertex.X * 64.0f / 32767.0f, 15.99f, 16.01f));
    }

    [Fact]
    public void Exact_owned_surface_is_not_duplicated_by_the_lod_side()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var exactStone = Build(world, (x, y, z) => y < 8 ? (byte)stone : (byte)0,
            chunkX: 12, chunkZ: -6);
        var lodAir = Build(world, (x, y, z) => 0, chunkX: 13, chunkZ: -6);

        var lodOwned = TerrainLodSeamMeshBuilder.BuildSolid(
            TerrainLodBoundarySummary.Capture(exactStone, 0, 4), 0,
            TerrainLodBoundarySummary.Capture(lodAir, 0, 4), 0,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true,
            materialSide: TerrainLodSeamMaterialSide.Neighbor);

        Assert.Empty(lodOwned.Vertices);
        Assert.Empty(lodOwned.Lights);
    }

    [Fact]
    public void Exact_to_lod_liquid_boundary_keeps_the_lod_height_and_winding()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var exactAir = Build(world, (x, y, z) => 0, chunkX: -2, chunkZ: 11);
        var lodWater = Build(
            world, (x, y, z) => y == 20 ? (byte)water : (byte)0,
            (x, y, z) => y == 20 ? (byte)4 : (byte)0,
            chunkX: -1, chunkZ: 11);

        var seam = TerrainLodSeamMeshBuilder.BuildTranslucent(
            TerrainLodBoundarySummary.Capture(exactAir, 0, 4), 0,
            TerrainLodBoundarySummary.Capture(lodWater, 0, 4), 0,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true,
            materialSide: TerrainLodSeamMaterialSide.Neighbor);

        Assert.Equal(16 * 4, seam.Vertices.Length);
        Assert.True(FaceNormalX(seam.Vertices) < 0);
        var highestWorldY = seam.Vertices.Max(vertex =>
            vertex.Y * 64.0f / 32767.0f + ChuckFormat.WorldHeight / 2.0f);
        Assert.InRange(highestWorldY, 20.43f, 20.46f);
    }

    [Fact]
    public void Seam_rejects_non_adjacent_levels_and_columns()
    {
        var world = new FakeWorldContext();
        var first = TerrainLodBoundarySummary.Capture(
            Build(world, (x, y, z) => 0, chunkX: 0, chunkZ: 0), 0, 4);
        var adjacent = TerrainLodBoundarySummary.Capture(
            Build(world, (x, y, z) => 0, chunkX: 1, chunkZ: 0), 0, 4);
        var distant = TerrainLodBoundarySummary.Capture(
            Build(world, (x, y, z) => 0, chunkX: 2, chunkZ: 0), 0, 4);

        Assert.Throws<ArgumentException>(() => TerrainLodSeamMeshBuilder.BuildSolid(
            first, 0, adjacent, 2, OmniBlock.Blocks.Side.East,
            world.Content.Blocks, true));
        Assert.Throws<ArgumentException>(() => TerrainLodSeamMeshBuilder.BuildSolid(
            first, 0, distant, 1, OmniBlock.Blocks.Side.East,
            world.Content.Blocks, true));
    }

    [Fact]
    public void Equal_liquid_boundaries_do_not_create_an_internal_blended_wall()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var west = Build(world, (x, y, z) => y == 12 ? (byte)water : (byte)0,
            chunkX: 0, chunkZ: 0);
        var east = Build(world, (x, y, z) => y == 12 ? (byte)water : (byte)0,
            chunkX: 1, chunkZ: 0);

        var seam = TerrainLodSeamMeshBuilder.BuildTranslucent(
            TerrainLodBoundarySummary.Capture(west, 0, 4), 0,
            TerrainLodBoundarySummary.Capture(east, 0, 4), 0,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true);

        Assert.Empty(seam.Vertices);
        Assert.Empty(seam.Lights);
    }

    [Fact]
    public void Mixed_detail_equal_liquid_boundaries_do_not_create_an_internal_wall()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var west = Build(world, (x, y, z) => y < 8 ? (byte)water : (byte)0,
            chunkX: 0, chunkZ: 0);
        var east = Build(world, (x, y, z) => y < 8 ? (byte)water : (byte)0,
            chunkX: 1, chunkZ: 0);

        var seam = TerrainLodSeamMeshBuilder.BuildTranslucent(
            TerrainLodBoundarySummary.Capture(west, 0, 4), 0,
            TerrainLodBoundarySummary.Capture(east, 1, 4), 1,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true);

        Assert.Empty(seam.Vertices);
        Assert.Empty(seam.Lights);
    }

    [Fact]
    public void Liquid_metadata_does_not_create_internal_voxel_walls()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var hierarchy = Build(
            world,
            (x, y, z) => y < 8 ? (byte)water : (byte)0,
            (x, y, z) => y < 8 && x >= 8 ? (byte)4 : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(
            hierarchy, 0, world.Content.Blocks, true);

        Assert.False(HasQuadOnPlane(mesh.TranslucentVertices, axis: 0, position: 8));
    }

    [Fact]
    public void Different_liquid_heights_create_only_the_reconciliation_strip()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var west = Build(
            world, (x, y, z) => y == 12 ? (byte)water : (byte)0,
            (x, y, z) => y == 12 ? (byte)0 : (byte)0,
            chunkX: -4, chunkZ: 7);
        var east = Build(
            world, (x, y, z) => y == 12 ? (byte)water : (byte)0,
            (x, y, z) => y == 12 ? (byte)6 : (byte)0,
            chunkX: -3, chunkZ: 7);

        var seam = TerrainLodSeamMeshBuilder.BuildTranslucent(
            TerrainLodBoundarySummary.Capture(west, 0, 4), 0,
            TerrainLodBoundarySummary.Capture(east, 0, 4), 0,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true);

        Assert.Equal(16 * 4, seam.Vertices.Length);
        Assert.Equal(seam.Vertices.Length, seam.Lights.Length);
        var worldY = seam.Vertices
            .Select(vertex => vertex.Y * 64.0f / 32767.0f + ChuckFormat.WorldHeight / 2.0f)
            .ToArray();
        Assert.InRange(worldY.Max() - worldY.Min(), 0.65f, 0.68f);
    }

    [Fact]
    public void Liquid_to_air_seam_preserves_the_metadata_surface_height()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var west = Build(
            world, (x, y, z) => y == 20 ? (byte)water : (byte)0,
            (x, y, z) => y == 20 ? (byte)4 : (byte)0,
            chunkX: 2, chunkZ: -5);
        var east = Build(world, (x, y, z) => 0, chunkX: 3, chunkZ: -5);

        var seam = TerrainLodSeamMeshBuilder.BuildTranslucent(
            TerrainLodBoundarySummary.Capture(west, 0, 4), 0,
            TerrainLodBoundarySummary.Capture(east, 1, 4), 1,
            OmniBlock.Blocks.Side.East, world.Content.Blocks, true);

        Assert.Equal(16 * 4, seam.Vertices.Length);
        var highestWorldY = seam.Vertices.Max(vertex =>
            vertex.Y * 64.0f / 32767.0f + ChuckFormat.WorldHeight / 2.0f);
        Assert.InRange(highestWorldY, 20.43f, 20.46f);
    }

    [Fact]
    public void Translucent_column_mesh_leaves_its_boundary_to_the_seam_artifact()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var liquid = Build(world, (x, y, z) => y < 8 ? (byte)water : (byte)0);
        var air = Build(world, (x, y, z) => 0);
        var airBoundary = TerrainLodBoundarySummary.Capture(air, 2, 4);

        var withoutEvidence = TerrainLodMeshBuilder.Build(
            liquid, 2, world.Content.Blocks, true);
        Assert.True(airBoundary.HasLevel(2));
        Assert.False(HasQuadOnPlane(
            withoutEvidence.TranslucentVertices, axis: 2, position: 16));
        Assert.Equal(withoutEvidence.TranslucentVertices.Length,
            withoutEvidence.TranslucentLights.Length);
    }

    [Fact]
    public void Glass_is_retained_by_the_translucent_slice()
    {
        var world = new FakeWorldContext();
        var glass = world.Content.Blocks.Get("omniblock:glass").Id;
        var hierarchy = Build(world, (x, y, z) => y < 4 ? (byte)glass : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 2, world.Content.Blocks, true);

        Assert.Empty(mesh.Vertices);
        Assert.NotEmpty(mesh.TranslucentVertices);
    }

    [Fact]
    public void Liquid_dominance_recovers_the_retained_solid_instead_of_cutting_a_hole()
    {
        TerrainLodMaterial water = new("omniblock:flowing_water", 0,
            TerrainLodGeometryClass.Liquid, false, 0);
        TerrainLodMaterial stone = new("omniblock:stone", 0,
            TerrainLodGeometryClass.Opaque, true, 0);
        TerrainLodCell cell = new(
            water, stone, default,
            20, 12, 0, byte.MaxValue, TerrainLodFaceMask.All);

        Assert.True(TerrainLodMeshBuilder.TrySelectDepthMaterial(cell, out var selected));
        Assert.Equal(stone, selected);
        Assert.True(TerrainLodMeshBuilder.TrySelectTranslucentMaterial(cell, out var translucent));
        Assert.Equal(water, translucent);
    }

    [Fact]
    public void Cutout_primary_material_remains_visible_in_the_depth_pass()
    {
        TerrainLodMaterial leaves = new("omniblock:leaves", 0,
            TerrainLodGeometryClass.Cutout, false, 0);
        TerrainLodCell cell = new(
            leaves, default, default,
            8, 0, 0, byte.MaxValue, TerrainLodFaceMask.All);

        Assert.True(TerrainLodMeshBuilder.TrySelectDepthMaterial(cell, out var selected));
        Assert.Equal(leaves, selected);
    }

    [Theory]
    [InlineData("omniblock:grass_block", 0)]
    [InlineData("omniblock:leaves", 0)]
    [InlineData("omniblock:leaves", 1)]
    public void Biome_and_metadata_tint_reaches_lod_vertices(string blockName, int metadata)
    {
        var world = new FakeWorldContext();
        var block = world.Content.Blocks.Get(blockName);
        var hierarchy = Build(
            world,
            (x, y, z) => y < 32 ? (byte)block.Id : (byte)0,
            (x, y, z) => y < 32 ? (byte)metadata : (byte)0);
        var chunk = world.ChunkHost.GetChunk(0, 0);
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < 32; y++)
        {
            chunk.Blocks[ChuckFormat.GetIndex(x, y, z)] = (byte)block.Id;
            chunk.Meta.SetNibble(x, y, z, metadata);
        }

        using var visuals = new WorldRegionSnapshot(
            world, 0, 0, 0, 15, ChuckFormat.WorldHeight - 1, 15);
        var tint = block.GetColorMultiplier(visuals, 2, 30, 2, metadata);
        var expected = TerrainLodMeshBuilder.PackTintedColor(tint, 1.0f);

        var mesh = TerrainLodMeshBuilder.Build(
            hierarchy, 2, world.Content.Blocks, true, visuals, visuals);

        Assert.Contains(mesh.Vertices, vertex => vertex.Color == expected);
    }

    [Fact]
    public void Grass_side_keeps_untinted_base_and_adds_tinted_overlay()
    {
        var world = new FakeWorldContext();
        var grass = world.Content.Blocks.Get("omniblock:grass_block");
        var overlay = OmniBlock.Textures.Atlases.Terrain.IndexOf(
            "omniblock:grass_block_side_overlay");

        var appearance = TerrainLodMeshBuilder.ResolveFaceAppearance(
            grass, 0, OmniBlock.Blocks.Side.North, 0x317F22, true, overlay);

        Assert.Equal(grass.GetTexture(OmniBlock.Blocks.Side.North, 0), appearance.Texture);
        Assert.Equal(0xFFFFFF, appearance.Tint);
        Assert.Equal(overlay, appearance.OverlayTexture);
        Assert.Equal(0x317F22, appearance.OverlayTint);
    }

    [Fact]
    public void Available_source_lighting_replaces_the_full_sky_fallback()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        var hierarchy = Build(world, (x, y, z) => y < 32 ? (byte)stone : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(
            hierarchy, 2, world.Content.Blocks, true, new ConstantLight(5, 7));

        Assert.NotEmpty(mesh.Lights);
        Assert.All(mesh.Lights, light =>
        {
            Assert.Equal(20, light.Sky);
            Assert.Equal(28, light.Block);
        });
    }

    [Fact]
    public void Captured_lighting_is_immutable_and_has_a_safe_exterior_fallback()
    {
        var world = new FakeWorldContext();
        var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], 3, -2);
        chunk.SkyLight.SetNibble(4, 70, 5, 9);
        chunk.BlockLight.SetNibble(4, 70, 5, 6);
        var captured = TerrainLodLightingSnapshot.Capture(chunk, 12, true);

        chunk.SkyLight.SetNibble(4, 70, 5, 0);
        chunk.BlockLight.SetNibble(4, 70, 5, 0);

        Assert.Equal(new LightLevels(9, 6),
            captured.GetLightLevels(3 * 16 + 4, 70, -2 * 16 + 5, 0));
        Assert.Equal(LightLevels.FullSky,
            captured.GetLightLevels(3 * 16 - 1, 70, -2 * 16 + 5, 0));
        Assert.Equal(12, captured.TerrainRevision);
    }

    [Theory]
    [InlineData(50, -1, 0)]
    [InlineData(100, -1, 0)]
    [InlineData(110, 1, 0)]
    [InlineData(120, 1, 1)]
    [InlineData(140, -1, 1)]
    [InlineData(300, -1, 2)]
    [InlineData(600, -1, 3)]
    [InlineData(1200, -1, 4)]
    [InlineData(270, 1, 1)]
    [InlineData(260, 2, 2)]
    [InlineData(570, 2, 2)]
    [InlineData(530, 3, 3)]
    [InlineData(1050, 4, 4)]
    [InlineData(900, 4, 3)]
    public void Detail_selection_is_distance_based_and_hysteretic(
        double distance, int previous, int expected)
    {
        Assert.Equal(expected, TerrainLodDetailSelector.SelectLevel(distance, 4, previous));
    }

    [Fact]
    public void Detail_selection_never_requests_an_unavailable_level()
    {
        Assert.Equal(3, TerrainLodDetailSelector.SelectLevel(900, 3));
        Assert.Equal(1, TerrainLodDetailSelector.SelectLevel(900, 1));
    }

    [Fact]
    public void Natural_cave_fixture_stays_block_scale_after_exact_handoff()
    {
        // The generated opening at (24,76,33) is about 110 blocks from the fixed
        // comparison camera. Both directions must clear the 10% hysteresis band.
        Assert.Equal(0, TerrainLodDetailSelector.SelectLevel(
            110, 4, previousLevel: 1, viewportHeight: 1034));
        Assert.Equal(1, TerrainLodDetailSelector.SelectLevel(
            110, 4, previousLevel: 0, viewportHeight: 1034, detailDropoffScale: 0.75));
    }

    [Fact]
    public void Higher_resolution_retains_finer_detail_at_the_same_distance()
    {
        Assert.Equal(3, TerrainLodDetailSelector.SelectLevel(600, 4, viewportHeight: 480));
        Assert.Equal(2, TerrainLodDetailSelector.SelectLevel(600, 4, viewportHeight: 1080));
    }

    [Theory]
    [InlineData(0.5, 4)]
    [InlineData(1.0, 3)]
    [InlineData(2.0, 2)]
    public void Dropoff_scale_moves_the_detail_boundaries_without_skipping_levels(
        double scale, int expected)
    {
        Assert.Equal(expected,
            TerrainLodDetailSelector.SelectLevel(
                600, 4, verticalFovDegrees: 70, viewportHeight: 480,
                detailDropoffScale: scale));
    }

    [Theory]
    [InlineData(0, new[] { 2 }, 1)]
    [InlineData(4, new[] { 2 }, 3)]
    [InlineData(2, new[] { 1, 3 }, 2)]
    [InlineData(0, new[] { 0, 4 }, 2)]
    public void Neighbor_constraint_is_bounded_and_order_independent(
        int requested, int[] neighbors, int expected)
    {
        Assert.Equal(expected,
            TerrainLodNeighborLevelConstraint.Constrain(requested, neighbors));
        Array.Reverse(neighbors);
        Assert.Equal(expected,
            TerrainLodNeighborLevelConstraint.Constrain(requested, neighbors));
    }

    [Fact]
    public void Admission_fills_the_nearest_radial_coverage_before_the_horizon()
    {
        (int X, int Z)[] pending = [(20, 0), (2, 0), (0, -2), (-20, 0), (0, 20)];

        var admitted = TerrainLodAdmissionOrder.TakeNearest(
            pending, new Vector3D<double>(8, 192, 8), 3);

        Assert.Equal([(0, -2), (2, 0), (-20, 0)], admitted);
    }

    private static TerrainLodHierarchy Build(
        FakeWorldContext world,
        Func<int, int, int, byte> block,
        Func<int, int, int, byte>? metadataAt = null,
        int chunkX = 0,
        int chunkZ = 0)
    {
        var blocks = new byte[ChuckFormat.ChunkSize];
        var metadata = new byte[ChuckFormat.ChunkSize];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.WorldHeight; y++)
        {
            blocks[ChuckFormat.GetIndex(x, y, z)] = block(x, y, z);
            metadata[ChuckFormat.GetIndex(x, y, z)] = metadataAt?.Invoke(x, y, z) ?? 0;
        }
        var source = new TerrainLodSourceSnapshot(
            chunkX, chunkZ, 16, ChuckFormat.WorldHeight, 16, blocks, metadata, 7);
        return TerrainLodReducer.Build(
            source,
            TerrainLodMaterialCatalog.FromRuntime(world.Content),
            TerrainLodReductionStrategy.SurfacePreserving);
    }

    private static long FaceNormalX(ChunkVertex[] vertices)
    {
        Assert.True(vertices.Length >= 3);
        var abY = (long)vertices[1].Y - vertices[0].Y;
        var abZ = (long)vertices[1].Z - vertices[0].Z;
        var acY = (long)vertices[2].Y - vertices[0].Y;
        var acZ = (long)vertices[2].Z - vertices[0].Z;
        return abY * acZ - abZ * acY;
    }

    private static float WorldY(ChunkVertex vertex) =>
        vertex.Y * 64.0f / 32767.0f + ChuckFormat.WorldHeight / 2.0f;

    private static bool HasQuadOnPlane(ChunkVertex[] vertices, int axis, float position)
    {
        for (var i = 0; i < vertices.Length; i += 4)
        {
            var onPlane = true;
            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = vertices[i + corner];
                var coordinate = axis switch
                {
                    0 => vertex.X,
                    1 => vertex.Y,
                    _ => vertex.Z
                } * 64.0f / 32767.0f;
                onPlane &= Math.Abs(coordinate - position) < 0.01f;
            }
            if (onPlane) return true;
        }
        return false;
    }

    private sealed class ConstantLight(byte sky, byte block) : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => 1;
        public float GetLuminance(int x, int y, int z) => 1;
        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
            new(sky, Math.Max(block, (byte)Math.Clamp(minBlockLight, 0, 15)));
    }

    private sealed class HeightLight(int skyStartY) : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => 1;
        public float GetLuminance(int x, int y, int z) => 1;
        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
            new(y >= skyStartY ? (byte)15 : (byte)0,
                (byte)Math.Clamp(minBlockLight, 0, 15));
    }
}
