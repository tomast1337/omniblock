using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodMeshBuilderTests
{
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
    }

    [Fact]
    public void Liquid_only_hierarchy_is_not_submitted_by_the_opaque_slice()
    {
        var world = new FakeWorldContext();
        var water = world.Content.Blocks.Get("omniblock:flowing_water").Id;
        var hierarchy = Build(world, (x, y, z) => y < 8 ? (byte)water : (byte)0);

        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 2, world.Content.Blocks, true);

        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Lights);
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
        var captured = CapturedChunkLighting.Capture(chunk, 12, true);

        chunk.SkyLight.SetNibble(4, 70, 5, 0);
        chunk.BlockLight.SetNibble(4, 70, 5, 0);

        Assert.Equal(new LightLevels(9, 6),
            captured.GetLightLevels(3 * 16 + 4, 70, -2 * 16 + 5, 0));
        Assert.Equal(LightLevels.FullSky,
            captured.GetLightLevels(3 * 16 - 1, 70, -2 * 16 + 5, 0));
        Assert.Equal(12, captured.TerrainRevision);
    }

    [Theory]
    [InlineData(100, -1, 2)]
    [InlineData(600, -1, 3)]
    [InlineData(1200, -1, 4)]
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
    }

    [Fact]
    public void Higher_resolution_retains_finer_detail_at_the_same_distance()
    {
        Assert.Equal(3, TerrainLodDetailSelector.SelectLevel(600, 4, viewportHeight: 480));
        Assert.Equal(2, TerrainLodDetailSelector.SelectLevel(600, 4, viewportHeight: 1080));
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
        Func<int, int, int, byte> block)
    {
        var blocks = new byte[ChuckFormat.ChunkSize];
        var metadata = new byte[ChuckFormat.ChunkSize];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.WorldHeight; y++)
            blocks[ChuckFormat.GetIndex(x, y, z)] = block(x, y, z);
        var source = new TerrainLodSourceSnapshot(
            0, 0, 16, ChuckFormat.WorldHeight, 16, blocks, metadata, 7);
        return TerrainLodReducer.Build(
            source,
            TerrainLodMaterialCatalog.FromRuntime(world.Content),
            TerrainLodReductionStrategy.SurfacePreserving);
    }

    private sealed class ConstantLight(byte sky, byte block) : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => 1;
        public float GetLuminance(int x, int y, int z) => 1;
        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
            new(sky, Math.Max(block, (byte)Math.Clamp(minBlockLight, 0, 15)));
    }
}
