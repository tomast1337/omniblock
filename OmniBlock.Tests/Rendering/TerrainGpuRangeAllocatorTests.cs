using OmniBlock.Client.Rendering.Chunks;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainGpuRangeAllocatorTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(112, 48, 112, 0, 0, 0)]
    [InlineData(128, 64, 128, 1, 1, 1)]
    [InlineData(-16, 0, -16, -1, 0, -1)]
    [InlineData(-128, 0, -128, -1, 0, -1)]
    [InlineData(-144, -16, -144, -2, -1, -2)]
    public void Region_address_matches_eight_by_eight_columns_and_four_vertical_sections(
        int x, int y, int z, int regionX, int regionY, int regionZ)
    {
        var key = TerrainRenderRegionKey.FromSectionPosition(new Vector3D<int>(x, y, z));

        Assert.Equal(new TerrainRenderRegionKey(regionX, regionY, regionZ), key);
        Assert.Equal(new Vector3D<int>(
            regionX * TerrainRenderRegionKey.WidthInBlocks,
            regionY * TerrainRenderRegionKey.HeightInBlocks,
            regionZ * TerrainRenderRegionKey.WidthInBlocks), key.Origin);
    }

    [Fact]
    public void Region_address_rejects_unaligned_section_positions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TerrainRenderRegionKey.FromSectionPosition(new Vector3D<int>(1, 0, 0)));
    }

    [Fact]
    public void Allocations_are_aligned_reused_and_fully_coalesced()
    {
        TerrainGpuRangeAllocator allocator = new(1024);

        Assert.True(allocator.TryAllocate(100, 64, out var first));
        Assert.True(allocator.TryAllocate(200, 64, out var second));
        Assert.Equal(0, first.OffsetBytes);
        Assert.Equal(128, second.OffsetBytes);

        allocator.Release(first);
        Assert.True(allocator.TryAllocate(64, 64, out var reused));
        Assert.Equal(0, reused.OffsetBytes);

        allocator.Release(reused);
        allocator.Release(second);
        var snapshot = allocator.Snapshot();
        Assert.Equal(0, snapshot.AllocatedBytes);
        Assert.Equal(1024, snapshot.FreeBytes);
        Assert.Equal(1024, snapshot.LargestFreeRangeBytes);
        Assert.Equal(1, snapshot.FreeRanges);
        Assert.Equal(0, snapshot.ExternalFragmentation);
    }

    [Fact]
    public void Allocations_support_non_power_of_two_vertex_stride_alignment()
    {
        TerrainGpuRangeAllocator allocator = new(1024);

        Assert.True(allocator.TryAllocate(100, 80, out var first));
        Assert.True(allocator.TryAllocate(100, 80, out var second));

        Assert.Equal(0, first.OffsetBytes);
        Assert.Equal(160, second.OffsetBytes);
        Assert.Equal(0, first.OffsetBytes % 80);
        Assert.Equal(0, second.OffsetBytes % 80);
    }

    [Fact]
    public void Failed_candidate_does_not_mutate_live_or_free_ranges()
    {
        TerrainGpuRangeAllocator allocator = new(256);
        Assert.True(allocator.TryAllocate(192, 16, out _));
        var before = allocator.Snapshot();

        Assert.False(allocator.TryAllocate(80, 16, out _));

        var after = allocator.Snapshot();
        Assert.Equal(before.AllocatedBytes, after.AllocatedBytes);
        Assert.Equal(before.FreeBytes, after.FreeBytes);
        Assert.Equal(before.ActiveAllocations, after.ActiveAllocations);
        Assert.Equal(before.FreeRanges, after.FreeRanges);
        Assert.Equal(before.FailedAllocations + 1, after.FailedAllocations);
    }

    [Fact]
    public void Released_handle_cannot_be_released_twice()
    {
        TerrainGpuRangeAllocator allocator = new(256);
        Assert.True(allocator.TryAllocate(64, 16, out var allocation));
        allocator.Release(allocation);

        Assert.Throws<InvalidOperationException>(() => allocator.Release(allocation));
    }

    [Fact]
    public void Retired_range_is_not_reused_until_the_presentation_boundary()
    {
        TerrainGpuRangeAllocator allocator = new(256);
        Assert.True(allocator.TryAllocate(128, 16, out var oldPresentation));
        allocator.Retire(oldPresentation);

        var pending = allocator.Snapshot();
        Assert.Equal(0, pending.ActiveAllocations);
        Assert.Equal(1, pending.PendingRetirements);
        Assert.Equal(128, pending.FreeBytes);
        Assert.Throws<InvalidOperationException>(() => allocator.Retire(oldPresentation));
        Assert.False(allocator.TryAllocate(192, 16, out _));

        Assert.Equal(1, allocator.ReleaseRetired());
        Assert.True(allocator.TryAllocate(192, 16, out var replacement));
        Assert.Equal(0, replacement.OffsetBytes);
        var after = allocator.Snapshot();
        Assert.Equal(1, after.ActiveAllocations);
        Assert.Equal(0, after.PendingRetirements);
    }

    [Fact]
    public void Compaction_plan_is_packed_and_does_not_mutate_the_live_allocator()
    {
        TerrainGpuRangeAllocator allocator = new(1024);
        Assert.True(allocator.TryAllocate(100, 64, out var first));
        Assert.True(allocator.TryAllocate(100, 64, out var hole));
        Assert.True(allocator.TryAllocate(100, 64, out var third));
        allocator.Release(hole);
        var before = allocator.Snapshot();

        var plan = allocator.CreateCompactionPlan(64);

        Assert.Equal(2, plan.Moves.Count);
        Assert.Equal(first, plan.Moves[0].Source);
        Assert.Equal(0, plan.Moves[0].DestinationOffsetBytes);
        Assert.Equal(third, plan.Moves[1].Source);
        Assert.Equal(128, plan.Moves[1].DestinationOffsetBytes);
        Assert.Equal(228, plan.RequiredBytes);
        Assert.Equal(before, allocator.Snapshot());
    }

    [Fact]
    public void Fragmentation_reports_free_space_that_cannot_form_one_range()
    {
        TerrainGpuRangeAllocator allocator = new(512);
        Assert.True(allocator.TryAllocate(128, 1, out var first));
        Assert.True(allocator.TryAllocate(128, 1, out var middle));
        Assert.True(allocator.TryAllocate(128, 1, out _));
        allocator.Release(first);
        allocator.Release(middle);

        var snapshot = allocator.Snapshot();
        // The adjacent first and middle ranges coalesce; the live third allocation separates that
        // 256-byte range from the final 128 bytes.
        Assert.Equal(384, snapshot.FreeBytes);
        Assert.Equal(256, snapshot.LargestFreeRangeBytes);
        Assert.InRange(snapshot.ExternalFragmentation, 0.333, 0.334);
    }
}
