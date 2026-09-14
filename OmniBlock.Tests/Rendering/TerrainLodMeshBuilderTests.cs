using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

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

    [Theory]
    [InlineData(100, -1, 2)]
    [InlineData(300, -1, 3)]
    [InlineData(700, -1, 4)]
    [InlineData(270, 2, 2)]
    [InlineData(240, 3, 3)]
    [InlineData(500, 4, 4)]
    [InlineData(400, 4, 3)]
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
}
