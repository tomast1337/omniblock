using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodConversionServiceTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x707070)
    ]);

    [Fact]
    public async Task Completed_result_retains_the_immutable_source_lighting()
    {
        var sky = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        var block = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        sky.SetNibble(2, 64, 3, 10);
        block.SetNibble(2, 64, 3, 7);
        var lighting = new TerrainLodLightingSnapshot(
            4, -2, 9, sky.Bytes, block.Bytes, hasSkyLight: true);
        var blocks = Enumerable.Repeat((byte)1, 8).ToArray();
        var source = new TerrainLodSourceSnapshot(
            4, -2, 2, 2, 2, blocks, new byte[8], 9, lighting);
        using var service = Service(2, Convert);

        service.Submit(source);
        sky.SetNibble(2, 64, 3, 0);
        block.SetNibble(2, 64, 3, 0);
        var result = await Take(service);

        Assert.Same(lighting, result.Lighting);
        Assert.Equal(new LightLevels(10, 7),
            result.Lighting!.GetLightLevels(4 * 16 + 2, 64, -2 * 16 + 3, 0));
    }

    [Fact]
    public async Task Capacity_is_bounded_but_an_owned_coordinate_can_coalesce()
    {
        var gate = new ManualResetEventSlim();
        using var service = Service(2, source =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            return Convert(source);
        });

        Assert.Equal(TerrainLodAdmissionResult.Accepted, service.Submit(Source(0, 0, 1)));
        await WaitUntil(() => service.Snapshot().Running == 1);
        Assert.Equal(TerrainLodAdmissionResult.Accepted, service.Submit(Source(1, 0, 1)));
        Assert.Equal(TerrainLodAdmissionResult.RejectedAtCapacity,
            service.Submit(Source(2, 0, 1)));
        Assert.Equal(TerrainLodAdmissionResult.Coalesced,
            service.Submit(Source(1, 0, 2)));

        var snapshot = service.Snapshot();
        Assert.Equal(2, snapshot.OwnedChunks);
        Assert.Equal(1, snapshot.RejectedCapacitySubmissions);
        Assert.Equal(1, snapshot.CoalescedSubmissions);
        Assert.True(snapshot.PeakRetainedSourceBytes <= 3 * SourceBytes);
        gate.Set();
    }

    [Fact]
    public async Task Newer_revision_during_conversion_discards_stale_output_and_rebuilds()
    {
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new ManualResetEventSlim();
        var attempts = 0;
        using var service = Service(4, source =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstEntered.TrySetResult();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return Convert(source);
        });

        service.Submit(Source(4, -7, 1));
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.Submit(Source(4, -7, 2));
        releaseFirst.Set();

        var result = await Take(service);
        Assert.Equal(2, result.TerrainRevision);
        Assert.Equal(2, attempts);
        Assert.Equal(1, service.Snapshot().StaleBuildsDiscarded);
        Assert.Equal(1, service.Snapshot().CompletedConversions);
    }

    [Fact]
    public async Task Failure_remains_observable_and_can_be_retried()
    {
        var attempts = 0;
        using var service = Service(2, source =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidDataException("fixture conversion failed");
            return Convert(source);
        });

        service.Submit(Source(-2, 9, 5));
        await WaitUntil(() => service.Snapshot().Failed == 1);
        Assert.True(service.TryGetFailure(-2, 9, out var error));
        Assert.Equal("fixture conversion failed", error);
        Assert.True(service.Retry(-2, 9));

        var result = await Take(service);
        Assert.Equal(5, result.TerrainRevision);
        Assert.Equal(1, service.Snapshot().FailedConversions);
        Assert.Equal(1, service.Snapshot().RetriedConversions);
    }

    [Fact]
    public async Task Older_revision_is_rejected_without_replacing_ready_data()
    {
        using var service = Service(2, Convert);
        service.Submit(Source(1, 2, 10));
        await WaitUntil(() => service.Snapshot().Ready == 1);

        Assert.Equal(TerrainLodAdmissionResult.RejectedStaleRevision,
            service.Submit(Source(1, 2, 9)));
        var result = await Take(service);

        Assert.Equal(10, result.TerrainRevision);
        Assert.Equal(1, service.Snapshot().RejectedStaleSubmissions);
    }

    [Fact]
    public async Task Completed_hierarchy_contains_every_affected_ancestor_level()
    {
        using var service = new TerrainLodConversionService(3, Materials, capacity: 2);
        service.Submit(Source(8, 9, 1, width: 8, height: 8, depth: 8));

        var result = await Take(service);

        Assert.Equal(new[] { 8, 4, 2, 1 },
            result.Hierarchy.Levels.Select(static level => level.Width));
        Assert.Equal(3, result.Dimension);
        Assert.Equal(0, service.Snapshot().OwnedChunks);
    }

    private static TerrainLodConversionService Service(
        int capacity,
        Func<TerrainLodSourceSnapshot, TerrainLodHierarchy> convert) =>
        new(0, capacity, convert);

    private static TerrainLodHierarchy Convert(TerrainLodSourceSnapshot source) =>
        TerrainLodReducer.Build(source, Materials, TerrainLodReductionStrategy.SurfacePreserving);

    private static TerrainLodSourceSnapshot Source(
        int x,
        int z,
        long revision,
        int width = 2,
        int height = 2,
        int depth = 2)
    {
        var blocks = Enumerable.Repeat((byte)1, width * height * depth).ToArray();
        return new TerrainLodSourceSnapshot(x, z, width, height, depth,
            blocks, new byte[blocks.Length], revision);
    }

    private static long SourceBytes => Source(0, 0, 0).EstimatedBytes;

    private static async Task<TerrainLodConversionResult> Take(
        TerrainLodConversionService service)
    {
        TerrainLodConversionResult? result = null;
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
