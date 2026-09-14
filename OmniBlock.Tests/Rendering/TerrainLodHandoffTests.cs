using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodHandoffTests
{
    [Fact]
    public void Linear_fog_is_extended_once_for_both_near_and_lod_terrain()
    {
        var source = new FogState(
            FogCurve.Linear, new Vector4D<float>(0.2f, 0.3f, 0.4f, 1), 32, 128, 1);

        var resolved = TerrainLodFog.Resolve(
            source, renderDistance: 8, terrainHorizonDistance: 64, fogDistance: 48);

        Assert.Equal(source.Start, resolved.Start);
        Assert.Equal(48 * 16, resolved.End);
        Assert.Equal(source.Color, resolved.Color);
    }

    [Fact]
    public void Submersion_fog_is_not_reinterpreted_as_horizon_fog()
    {
        var source = new FogState(
            FogCurve.Exponential, new Vector4D<float>(0.02f, 0.02f, 0.2f, 1), 0, 1, 0.1f);

        Assert.Equal(source, TerrainLodFog.Resolve(
            source, renderDistance: 8, terrainHorizonDistance: 64, fogDistance: 48));
    }

    [Fact]
    public void Incomplete_near_column_keeps_complete_lod_coverage()
    {
        TerrainLodHandoffTransition transition = default;

        transition.Update(nearPresent: true, nearReady: false, 0.1f, fadeEnabled: true);

        Assert.Equal(TerrainLodHandoffState.NearPreparing, transition.State);
        Assert.Equal(0, transition.Progress);
    }

    [Fact]
    public void Ready_near_column_crosses_a_bounded_overlap_before_takeover()
    {
        TerrainLodHandoffTransition transition = default;

        transition.Update(nearPresent: true, nearReady: true,
            TerrainLodHandoffTransition.DurationSeconds / 2, fadeEnabled: true);

        Assert.Equal(TerrainLodHandoffState.Overlap, transition.State);
        Assert.Equal(0.5f, transition.Progress, 3);
        Assert.Equal(1, transition.Started);

        transition.Update(nearPresent: true, nearReady: true,
            TerrainLodHandoffTransition.DurationSeconds / 2, fadeEnabled: true);

        Assert.Equal(TerrainLodHandoffState.NearOnly, transition.State);
        Assert.Equal(1, transition.Progress);
        Assert.Equal(1, transition.Started);
    }

    [Fact]
    public void Readiness_regression_restores_lod_without_a_partial_coverage_hole()
    {
        TerrainLodHandoffTransition transition = default;
        transition.Update(nearPresent: true, nearReady: true,
            TerrainLodHandoffTransition.DurationSeconds / 2, fadeEnabled: true);

        transition.Update(nearPresent: true, nearReady: false, 0.01f, fadeEnabled: true);

        Assert.Equal(TerrainLodHandoffState.NearPreparing, transition.State);
        Assert.Equal(0, transition.Progress);
        Assert.Equal(1, transition.Reversals);
    }

    [Fact]
    public void Disabled_fade_switches_only_when_replacement_is_ready()
    {
        TerrainLodHandoffTransition transition = default;

        transition.Update(nearPresent: true, nearReady: false, 1, fadeEnabled: false);
        Assert.Equal(0, transition.Progress);

        transition.Update(nearPresent: true, nearReady: true, 0, fadeEnabled: false);
        Assert.Equal(TerrainLodHandoffState.NearOnly, transition.State);
        Assert.Equal(1, transition.Progress);
    }

    [Fact]
    public void Detail_levels_overlap_before_the_new_level_becomes_authoritative()
    {
        TerrainLodLevelTransition transition = default;
        var initial = transition.Update(2, 0, fadeEnabled: true);
        var overlap = transition.Update(
            1, TerrainLodLevelTransition.DurationSeconds / 2, fadeEnabled: true);

        Assert.False(initial.Active);
        Assert.True(overlap.Active);
        Assert.Equal(2, overlap.PrimaryLevel);
        Assert.Equal(1, overlap.SecondaryLevel);
        Assert.Equal(0.5f, overlap.Progress, 3);
        Assert.Equal(1, overlap.DominantLevel);
    }

    [Fact]
    public void Detail_transition_reverses_without_restarting_from_an_endpoint()
    {
        TerrainLodLevelTransition transition = default;
        transition.Update(2, 0, fadeEnabled: true);
        transition.Update(1, TerrainLodLevelTransition.DurationSeconds / 4, fadeEnabled: true);

        transition.Update(2, 0.02f, fadeEnabled: true);
        transition.Update(2, 0.02f, fadeEnabled: true);
        var reversed = transition.Update(2, 0.02f, fadeEnabled: true);

        Assert.True(reversed.Active);
        Assert.Equal(1, reversed.PrimaryLevel);
        Assert.Equal(2, reversed.SecondaryLevel);
        Assert.Equal(0.67f, reversed.Progress, 3);
        Assert.Equal(1, transition.Reversals);
    }

    [Fact]
    public void One_frame_target_noise_does_not_reverse_an_active_transition()
    {
        TerrainLodLevelTransition transition = default;
        transition.Update(2, 0, fadeEnabled: true);
        transition.Update(1, 0.02f, fadeEnabled: true);

        for (var frame = 0; frame < 12; frame++)
            transition.Update(frame % 2 == 0 ? 2 : 1, 0.01f, fadeEnabled: true);

        Assert.Equal(0, transition.Reversals);
    }

    [Fact]
    public void A_stable_detail_request_does_not_restart_or_reverse_the_transition()
    {
        TerrainLodLevelTransition transition = default;
        transition.Update(3, 0, fadeEnabled: true);

        for (var frame = 0; frame < 30; frame++)
            transition.Update(2, 1 / 120.0f, fadeEnabled: true);

        var settled = transition.Update(2, 1, fadeEnabled: true);

        Assert.False(settled.Active);
        Assert.Equal(2, settled.PrimaryLevel);
        Assert.Equal(1, transition.Started);
        Assert.Equal(0, transition.Reversals);
    }
}
