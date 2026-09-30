using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodStairBorderTests
{
    [Fact]
    public void One_cell_neighbor_strip_selects_the_corner_without_retaining_the_neighbor_tile()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stair = checked((byte)world.Content.Blocks.Get("wooden_stairs").Id);
        var owner = Leaf(materials, 0, 0, stair, 15, 8, 7, 0, 1);
        var east = Leaf(materials, 1, 0, stair, 0, 8, 7, 2, 1);
        var border = TerrainLodStairBorder.Capture(owner,
            key => key == east.Key ? east : null);
        Assert.Equal(2, border.StairAt(16, 7, 8, 16));
        Assert.Null(border.StairAt(-1, 7, 8, 16));

        var straight = TerrainLodSpatialMeshBuilder.Build(owner, world.Content.Blocks, 8);
        var corner = TerrainLodSpatialMeshBuilder.Build(owner, world.Content.Blocks, 8,
            stairBorder: border);
        Assert.Equal(border.Identity, corner.StairBorderIdentity);
        Assert.NotEqual(straight.SolidQuadCount, corner.SolidQuadCount);

        var sameEdge = Leaf(materials, 1, 0, stair, 0, 8, 7, 2, 2,
            extraBlock: (3, 8, 3, checked((byte)world.Content.Blocks.Get("stone").Id)));
        Assert.Equal(border.Identity, TerrainLodStairBorder.Capture(owner,
            key => key == sameEdge.Key ? sameEdge : null).Identity);
        var changedEdge = Leaf(materials, 1, 0, stair, 0, 8, 7, 0, 3);
        Assert.NotEqual(border.Identity, TerrainLodStairBorder.Capture(owner,
            key => key == changedEdge.Key ? changedEdge : null).Identity);
    }

    [Fact]
    public async Task Changed_neighbor_edge_supersedes_an_in_flight_spatial_candidate()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stair = checked((byte)world.Content.Blocks.Get("wooden_stairs").Id);
        var owner = Leaf(materials, 0, 0, stair, 15, 8, 7, 0, 1);
        var firstNeighbor = Leaf(materials, 1, 0, stair, 0, 8, 7, 2, 1);
        var secondNeighbor = Leaf(materials, 1, 0, stair, 0, 8, 7, 0, 2);
        var first = TerrainLodStairBorder.Capture(owner,
            key => key == firstNeighbor.Key ? firstNeighbor : null);
        var second = TerrainLodStairBorder.Capture(owner,
            key => key == secondNeighbor.Key ? secondNeighbor : null);
        using var service = new TerrainLodSpatialMeshCompilationService(2, 1);
        Assert.Equal(TerrainLodSpatialMeshAdmissionResult.Accepted,
            service.Submit(owner, world.Content.Blocks, 8,
                TerrainLodSpatialMeshWorkKind.Coverage, 1, stairBorder: first));
        Assert.Equal(TerrainLodSpatialMeshAdmissionResult.Coalesced,
            service.Submit(owner, world.Content.Blocks, 8,
                TerrainLodSpatialMeshWorkKind.Refinement, 1, stairBorder: second));
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        TerrainLodSpatialMeshCompilationResult? result;
        while (!service.TryTakeCompleted(out result))
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10));
            await Task.Delay(5);
        }
        Assert.Null(result!.Failure);
        Assert.Equal(second.Identity, result.Mesh!.StairBorderIdentity);
    }

    private static TerrainLodColumnTile Leaf(TerrainLodMaterialCatalog materials,
        int chunkX, int chunkZ, byte block, int x, int y, int z, byte meta, long revision,
        (int X, int Y, int Z, byte Block)? extraBlock = null)
    {
        const int height = 16;
        var blocks = new byte[16 * height * 16];
        var metadata = new byte[blocks.Length];
        var index = (x * 16 + z) * height + y;
        blocks[index] = block;
        metadata[index] = meta;
        if (extraBlock is { } extra)
            blocks[(extra.X * 16 + extra.Z) * height + extra.Y] = extra.Block;
        var source = new TerrainLodSourceSnapshot(chunkX, chunkZ, 16, height, 16,
            blocks, metadata, terrainRevision: revision);
        return TerrainLodColumnTile.BuildLeaf(source, materials);
    }
}
