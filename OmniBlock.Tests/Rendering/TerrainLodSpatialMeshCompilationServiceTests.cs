using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialMeshCompilationServiceTests
{
    [Fact]
    public async Task Worker_compiles_an_immutable_tile_off_thread()
    {
        var world = new FakeWorldContext();
        var tile = Leaf(world, 0, 0, revision: 1);
        using var service = new TerrainLodSpatialMeshCompilationService(2, 1);

        Assert.Equal(TerrainLodSpatialMeshAdmissionResult.Accepted,
            service.Submit(tile, world.Content.Blocks, 8,
                TerrainLodSpatialMeshWorkKind.Coverage, 4));
        var result = await Take(service);

        Assert.Null(result.Failure);
        Assert.NotNull(result.Mesh);
        Assert.Equal(tile.CanonicalHash, result.Mesh.CanonicalHash);
        Assert.True(result.Mesh.SolidQuadCount > 0);
        Assert.True(result.CompilationMs >= 0);
        Assert.Equal(0, service.Snapshot().Owned);
    }

    [Fact]
    public async Task Newer_identity_coalesces_and_stale_mesh_never_publishes()
    {
        var world = new FakeWorldContext();
        var first = Leaf(world, 2, -1, revision: 1);
        var latest = Leaf(world, 2, -1, revision: 2);
        using var service = new TerrainLodSpatialMeshCompilationService(2, 1);

        service.Submit(first, world.Content.Blocks, 8,
            TerrainLodSpatialMeshWorkKind.Coverage, 2);
        Assert.Equal(TerrainLodSpatialMeshAdmissionResult.Coalesced,
            service.Submit(latest, world.Content.Blocks, 8,
                TerrainLodSpatialMeshWorkKind.Refinement, 2));
        var result = await Take(service);

        Assert.Equal(latest.CanonicalHash, result.Mesh!.CanonicalHash);
        Assert.NotEqual(first.CanonicalHash, result.Mesh.CanonicalHash);
        Assert.True(service.Snapshot().Coalesced > 0);
    }

    [Fact]
    public async Task Completed_capacity_applies_backpressure_to_the_worker()
    {
        var world = new FakeWorldContext();
        using var service = new TerrainLodSpatialMeshCompilationService(3, 1);
        service.Submit(Leaf(world, 0, 0, 1), world.Content.Blocks, 8,
            TerrainLodSpatialMeshWorkKind.Coverage, 1);
        service.Submit(Leaf(world, 1, 0, 1), world.Content.Blocks, 8,
            TerrainLodSpatialMeshWorkKind.Coverage, 2);

        await WaitUntil(() => service.Snapshot().Ready == 1);
        var blocked = service.Snapshot();
        Assert.Equal(1, blocked.Ready);
        Assert.Equal(1, blocked.Queued);
        Assert.Equal(0, blocked.Running);

        Assert.NotNull((await Take(service)).Mesh);
        Assert.NotNull((await Take(service)).Mesh);
    }

    private static TerrainLodColumnTile Leaf(
        FakeWorldContext world,
        int chunkX,
        int chunkZ,
        long revision)
    {
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var blocks = new byte[16 * 8 * 16];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < 4; y++)
            blocks[(x * 16 + z) * 8 + y] = stone;
        var source = new TerrainLodSourceSnapshot(
            chunkX, chunkZ, 16, 8, 16, blocks, new byte[blocks.Length], revision);
        return TerrainLodColumnTile.BuildLeaf(
            source, TerrainLodMaterialCatalog.FromRuntime(world.Content));
    }

    private static async Task<TerrainLodSpatialMeshCompilationResult> Take(
        TerrainLodSpatialMeshCompilationService service)
    {
        TerrainLodSpatialMeshCompilationResult? result = null;
        await WaitUntil(() => service.TryTakeCompleted(out result));
        return result!;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(1);
        }
    }
}
