using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class SectionMeshRebuildPlanTests
{
    [Fact]
    public void Isolated_edit_with_one_cell_halo_visits_one_quarter_section()
    {
        var plan = SectionMeshRebuildPlan.FromWorldYBounds(64, 65, 67);

        Assert.Equal(0b0001, plan.PageMask);
        Assert.Equal(1, plan.PageBuildCount);
        Assert.Equal(16 * 16 * 4, plan.BlockVisitCount);
        Assert.False(plan.IsFull);
    }

    [Fact]
    public void Halo_crossing_page_boundary_selects_both_pages()
    {
        var plan = SectionMeshRebuildPlan.FromWorldYBounds(64, 67, 69);

        Assert.Equal(0b0011, plan.PageMask);
        Assert.Equal(2, plan.PageBuildCount);
        Assert.Equal(16 * 16 * 8, plan.BlockVisitCount);
    }

    [Fact]
    public void Bounds_are_clamped_independently_for_adjacent_sections()
    {
        var lower = SectionMeshRebuildPlan.FromWorldYBounds(64, 79, 81);
        var upper = SectionMeshRebuildPlan.FromWorldYBounds(80, 79, 81);

        Assert.Equal(0b1000, lower.PageMask);
        Assert.Equal(0b0001, upper.PageMask);
    }

    [Fact]
    public void Coalesced_updates_union_their_page_masks()
    {
        var low = SectionMeshRebuildPlan.FromWorldYBounds(0, 1, 3);
        var high = SectionMeshRebuildPlan.FromWorldYBounds(0, 13, 15);

        var combined = low.Union(high);

        Assert.Equal(0b1001, combined.PageMask);
        Assert.Equal(2, combined.PageBuildCount);
    }

    [Fact]
    public void Unknown_dependencies_use_the_full_section_fallback()
    {
        var plan = SectionMeshRebuildPlan.FromWorldYBounds(0, 6, 8, dependenciesKnown: false);

        Assert.Equal(SectionMeshRebuildPlan.Full, plan);
        Assert.Equal(16 * 16 * 16, plan.BlockVisitCount);
    }
}
