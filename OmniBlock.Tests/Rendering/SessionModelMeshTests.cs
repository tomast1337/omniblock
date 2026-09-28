using System.Diagnostics;
using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;
using Xunit.Abstractions;

namespace OmniBlock.Tests.Rendering;

[CollectionDefinition("SessionModelMeshPerformance", DisableParallelization = true)]
public sealed class SessionModelMeshPerformanceCollection;

// The allocator uses shared pools. Other rendering tests borrowing those buffers would turn
// the allocation tripwire into a measurement of test-runner concurrency rather than bindings.
[Collection("SessionModelMeshPerformance")]
public sealed class SessionModelMeshTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Complete_section_build_preserves_packed_pages_and_has_no_binding_allocation_overhead(bool slabs)
    {
        var world = new FakeWorldContext();
        var block = world.Content.Blocks.Get(slabs ? "slab" : "stone");
        var chunk = world.ChunkHost.GetChunk(0, 0);
        for (var x = 0; x < 16; x++)
        for (var y = 64; y < 80; y++)
        for (var z = 0; z < 16; z++)
        {
            // Open faces and enclosed cells, not a trivial empty/single-block benchmark.
            if ((x + y + z) % 3 == 0) continue;
            chunk.Blocks[ChuckFormat.GetIndex(x, y, z)] = (byte)block.Id;
            chunk.Meta.SetNibble(x, y, z, slabs && (x + z) % 2 == 0 ? 8 : 0);
            chunk.SkyLight.SetNibble(x, y, z, 15);
        }
        using var snapshot = new WorldRegionSnapshot(world, -1, 63, -1, 16, 80, 16);
        using var generator = new ChunkMeshGenerator();
        var models = BlockModelBindingTests.Build(world.Content.Blocks);
        using var baseline = Build(null);
        using var bound = Build(models);
        Assert.Same(models, bound.Models);
        Assert.True(bound.RetainedBytes > 0);
        Assert.Equal(baseline.VisibilityData, bound.VisibilityData);
        Assert.Equal(baseline.RetainedBytes, bound.RetainedBytes);
        Assert.Equal(baseline.UploadBytes, bound.UploadBytes);
        for (var page = 0; page < baseline.Pages.Length; page++)
        {
            var old = baseline.Pages[page];
            var current = bound.Pages[page];
            Assert.NotNull(old.Solid);
            Assert.NotNull(current.Solid);
            Assert.Equal(MemoryMarshal.AsBytes(old.Solid.Span).ToArray(), MemoryMarshal.AsBytes(current.Solid.Span).ToArray());
            Assert.Equal(old.SolidRanges, current.SolidRanges);
            Assert.Null(current.Translucent);
        }

        var timings = new List<double>[] { [], [] };
        var allocations = new List<long>[] { [], [] };
        for (var i = 0; i < 8; i++)
        {
            using var warm = Build(i % 2 == 0 ? null : models);
        }
        for (var i = 0; i < 8; i++)
        for (var j = 0; j < 2; j++)
        {
            var path = (i + j) % 2;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            using var result = Build(path == 0 ? null : models);
            timings[path].Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            allocations[path].Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }
        // CPU timings are reported, not flaky pass/fail thresholds on a shared machine.
        timings[0].Sort(); timings[1].Sort();
        Assert.Equal(allocations[0].Min(), allocations[1].Min());
        output.WriteLine($"{block.Id} slabs={slabs}: full section median baseline={timings[0][4]:F3}ms, bound={timings[1][4]:F3}ms; allocations baseline={allocations[0].Min()}, bound={allocations[1].Min()} bytes.");

        MeshBuildResult Build(BlockModelBindings? bindings) => generator.GenerateMesh(new Vector3D<int>(0, 64, 0),
            1, snapshot, false, SectionMeshRebuildPlan.Full, CancellationToken.None, bindings);
    }
}
