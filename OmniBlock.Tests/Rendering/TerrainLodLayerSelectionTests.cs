using OmniBlock.Client.Rendering.Chunks.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodLayerSelectionTests
{
    private readonly record struct Layers(bool Solid, bool Translucent);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compiled_empty_layer_is_valid_coverage_not_a_reason_for_coarse_fallback(bool translucent)
    {
        Dictionary<int, Layers> levels = new()
        {
            [0] = translucent ? new(true, false) : new(false, true),
            [4] = new(true, true)
        };
        // Exercise either an empty solid or an empty translucent fine layer.
        var selected = Select(levels, 0, translucent);
        Assert.True(selected.Available);
        Assert.Equal(0, selected.Level);
        Assert.False(selected.HasGeometry);
    }

    [Fact]
    public void Missing_fine_level_falls_back_but_an_empty_uploaded_level_does_not()
    {
        Dictionary<int, Layers> levels = new() { [2] = new(true, true), [4] = new(true, true) };
        Assert.Equal(new TerrainLodLayerSelection(2, true), Select(levels, 0, true));
        levels[0] = new(true, false);
        Assert.Equal(new TerrainLodLayerSelection(0, false), Select(levels, 0, true));
        levels[0] = new(true, true);
        Assert.Equal(new TerrainLodLayerSelection(0, true), Select(levels, 0, true));
    }

    [Fact]
    public void All_empty_levels_are_available_but_missing_catalog_is_not()
    {
        Dictionary<int, Layers> levels = new() { [0] = default, [2] = default };
        Assert.Equal(new TerrainLodLayerSelection(0, false), Select(levels, 0, true));
        Assert.False(Select([], 0, true).Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_distance_prefers_finer_compiled_level_even_if_empty(bool reverse)
    {
        Dictionary<int, Layers> levels = reverse
            ? new() { [4] = new(true, true), [2] = new(true, false) }
            : new() { [2] = new(true, false), [4] = new(true, true) };
        Assert.Equal(new TerrainLodLayerSelection(2, false), Select(levels, 3, true));
    }

    [Fact]
    public void Empty_fine_neighbor_does_not_force_adjacent_water_to_coarse_detail()
    {
        Dictionary<int, Layers> levels = new() { [0] = new(true, false), [4] = new(true, true) };
        var neighbor = Select(levels, 0, true);
        Assert.Equal(0, TerrainLodNeighborLevelConstraint.Constrain(0, [neighbor.Level]));
    }

    [Fact]
    public void Coarse_geometry_can_fade_out_into_an_empty_fine_layer_and_back()
    {
        Dictionary<int, Layers> levels = new() { [0] = new(true, false), [4] = new(true, true) };
        TerrainLodLevelTransition transition = default;
        transition.Update(Select(levels, 4, true).Level, 0, fadeEnabled: true);
        var halfway = transition.Update(Select(levels, 0, true).Level,
            TerrainLodLevelTransition.DurationSeconds / 2, fadeEnabled: true);
        Assert.True(halfway.Active);
        Assert.True(Select(levels, halfway.PrimaryLevel, true).HasGeometry);
        Assert.False(Select(levels, halfway.SecondaryLevel, true).HasGeometry);
        var complete = transition.Update(0, TerrainLodLevelTransition.DurationSeconds, fadeEnabled: true);
        Assert.False(complete.Active);
        Assert.Equal(0, complete.PrimaryLevel);
        Assert.False(Select(levels, complete.PrimaryLevel, true).HasGeometry);
        var restored = transition.Update(4, TerrainLodLevelTransition.DurationSeconds, fadeEnabled: true);
        Assert.False(restored.Active);
        Assert.True(Select(levels, restored.PrimaryLevel, true).HasGeometry);
    }

    [Fact]
    public void Empty_layer_boundary_keeps_adjacent_water_seam_ownership()
    {
        var plan = TerrainLodSeamCoverage.Plan(
            ownerLodDrawn: false, ownerNearProgress: 0,
            neighborLodDrawn: true, neighborNearProgress: 0);
        Assert.False(plan.Owner);
        Assert.True(plan.Neighbor);
        Assert.False(plan.Combined);
    }

    private static TerrainLodLayerSelection Select(Dictionary<int, Layers> levels, int requested, bool translucent) =>
        translucent
            ? TerrainLodLayerSelection.Select(levels, requested, static layer => layer.Translucent)
            : TerrainLodLayerSelection.Select(levels, requested, static layer => layer.Solid);
}
