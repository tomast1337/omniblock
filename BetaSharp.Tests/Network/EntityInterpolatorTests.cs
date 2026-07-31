using BetaSharp.Client.Network;

namespace BetaSharp.Tests.Network;

/// <summary>
///     Gating and lifecycle. <see cref="EntityInterpolator.Apply" /> needs a live world and is
///     covered by <see cref="SnapshotBufferTests" /> for the arithmetic it delegates to.
/// </summary>
public sealed class EntityInterpolatorTests
{
    private static EntityInterpolator Available() => new() { Available = true };

    // ---- gating ----

    [Fact]
    public void Not_active_until_a_timeline_exists()
    {
        // Enabled by default, but nothing to sample against until the server stamps and the clock
        // syncs. Active must stay false or the legacy path gets skipped with no replacement.
        EntityInterpolator interpolator = new();

        Assert.True(interpolator.Enabled);
        Assert.False(interpolator.Available);
        Assert.False(interpolator.Active);
    }

    [Fact]
    public void Available_alone_does_not_activate_a_disabled_interpolator()
    {
        EntityInterpolator interpolator = new() { Available = true, Enabled = false };
        Assert.False(interpolator.Active);
    }

    [Fact]
    public void Active_requires_both()
    {
        Assert.True(Available().Active);
    }

    [Fact]
    public void An_entity_is_not_interpolating_while_the_timeline_is_missing()
    {
        // The regression this guards: if IsInterpolating returned true here, the caller would skip
        // the legacy retarget while Apply did nothing, and every remote entity would freeze.
        EntityInterpolator interpolator = new();
        interpolator.Record(1, serverTimeMs: 1000, 0, 0, 0, 0, 0);

        Assert.False(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void An_entity_is_interpolating_once_the_timeline_exists()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, serverTimeMs: 1000, 0, 0, 0, 0, 0);

        Assert.True(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void An_unknown_entity_is_never_interpolating()
    {
        Assert.False(Available().IsInterpolating(99));
    }

    // ---- recording ----

    [Fact]
    public void A_snapshot_without_a_timestamp_is_dropped()
    {
        // Zero means the server does not stamp. Recording it would put a snapshot on no timeline,
        // which is the guess this whole mechanism replaces.
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, serverTimeMs: 0, 0, 0, 0, 0, 0);

        Assert.Equal(0, interpolator.TrackedCount);
        Assert.False(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void Recording_tracks_one_buffer_per_entity()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(1, 1050, 1, 0, 0, 0, 0);
        interpolator.Record(2, 1050, 0, 0, 0, 0, 0);

        Assert.Equal(2, interpolator.TrackedCount);
    }

    // ---- lifecycle ----

    [Fact]
    public void Forget_drops_only_that_entity()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(2, 1000, 0, 0, 0, 0, 0);

        interpolator.Forget(1);

        Assert.Equal(1, interpolator.TrackedCount);
        Assert.False(interpolator.IsInterpolating(1));
        Assert.True(interpolator.IsInterpolating(2));
    }

    [Fact]
    public void Forgetting_an_untracked_entity_is_harmless()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Forget(42);

        Assert.Equal(0, interpolator.TrackedCount);
    }

    [Fact]
    public void Clear_drops_everything()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(2, 1000, 0, 0, 0, 0, 0);

        interpolator.Clear();

        Assert.Equal(0, interpolator.TrackedCount);
    }

    [Fact]
    public void The_default_delay_is_the_documented_floor()
    {
        // clamp(2 * tickInterval + 2 * jitter, 100, 500) with zero jitter. Two tick intervals at
        // 20 TPS is the minimum that keeps two snapshots bracketing render time.
        Assert.Equal(100, EntityInterpolator.DefaultDelayMs);
        Assert.Equal(EntityInterpolator.DefaultDelayMs, new EntityInterpolator().DelayMs);
    }
}
