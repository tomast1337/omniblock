using OmniBlock.Network.Messages;
using OmniBlock.Server.Entities;

namespace OmniBlock.Tests.Network;

/// <summary>
///     <see cref="EntityPositionHistory" />: the server's record of where a tracked entity has been,
///     and the rewind policy that reads it.
///     <para>
///         The property under test is that a reach check run against a rewound position agrees with
///         what the attacking client had on screen — see
///         <see cref="A_target_walking_away_is_still_within_reach_where_the_attacker_saw_it" />, which
///         is the whole of why this exists.
///     </para>
/// </summary>
public sealed class EntityPositionHistoryTests
{
    /// <summary>Server tick interval, so the fixtures below read as ticks rather than as numbers.</summary>
    private const long TickMs = 50;

    [Fact]
    public void An_empty_history_answers_nothing()
    {
        EntityPositionHistory history = new();

        Assert.False(history.Sample(1000, out var x, out var y, out var z));
        Assert.Equal(0.0, x);
        Assert.Equal(0.0, y);
        Assert.Equal(0.0, z);
    }

    [Fact]
    public void A_time_between_two_ticks_lands_between_the_two_positions()
    {
        EntityPositionHistory history = new();
        history.Record(1000, 0.0, 64.0, 0.0);
        history.Record(1000 + TickMs, 4.0, 64.0, 0.0);

        Assert.True(history.Sample(1000 + TickMs / 2, out var x, out var y, out var z));
        Assert.Equal(2.0, x, 6);
        Assert.Equal(64.0, y, 6);
        Assert.Equal(0.0, z, 6);
    }

    [Fact]
    public void A_time_past_the_newest_record_is_the_present()
    {
        EntityPositionHistory history = new();
        history.Record(1000, 0.0, 64.0, 0.0);
        history.Record(1050, 4.0, 64.0, 0.0);

        Assert.True(history.Sample(9999, out var x, out _, out _));
        Assert.Equal(4.0, x, 6);
    }

    [Fact]
    public void A_time_before_the_oldest_record_is_the_oldest_position()
    {
        EntityPositionHistory history = new();
        history.Record(1000, 7.0, 64.0, 0.0);
        history.Record(1050, 8.0, 64.0, 0.0);

        Assert.True(history.Sample(0, out var x, out _, out _));
        Assert.Equal(7.0, x, 6);
    }

    /// <summary>
    ///     The ring must drop the oldest rather than the newest. Reversing that would leave a history
    ///     that answers accurately about two seconds ago and not at all about now, which is the
    ///     opposite of what a rewind needs.
    /// </summary>
    [Fact]
    public void Beyond_capacity_the_oldest_ticks_are_the_ones_that_go()
    {
        EntityPositionHistory history = new();

        for (var tick = 0; tick < EntityPositionHistory.Capacity * 2; tick++)
        {
            history.Record(1000 + tick * TickMs, tick, 64.0, 0.0);
        }

        Assert.Equal(EntityPositionHistory.Capacity, history.Count);

        var newestStamp = 1000 + (EntityPositionHistory.Capacity * 2 - 1) * TickMs;
        Assert.True(history.Sample(newestStamp, out var x, out _, out _));
        Assert.Equal(EntityPositionHistory.Capacity * 2 - 1, x, 6);

        // Asking further back than the ring holds clamps to its oldest retained tick, not to zero.
        Assert.True(history.Sample(0, out var oldestX, out _, out _));
        Assert.Equal(EntityPositionHistory.Capacity, oldestX, 6);
    }

    /// <summary>
    ///     The tracker runs on a separate accumulator from the simulation, so it can be called twice
    ///     between two <c>Tick</c>s and hand over the same stamp. Appending both would give the
    ///     interpolation a zero-width interval.
    /// </summary>
    [Fact]
    public void The_same_instant_recorded_twice_is_one_record()
    {
        EntityPositionHistory history = new();
        history.Record(1000, 1.0, 64.0, 0.0);
        history.Record(1000, 2.0, 64.0, 0.0);

        Assert.Equal(1, history.Count);
        Assert.True(history.Sample(1000, out var x, out _, out _));
        Assert.Equal(2.0, x, 6);
    }

    [Fact]
    public void No_rewind_information_means_the_present()
    {
        Assert.Equal(5000, EntityPositionHistory.ClampRewind(0, 5000));
        Assert.Equal(5000, EntityPositionHistory.ClampRewind(-1, 5000));
    }

    /// <summary>
    ///     The bound that stops a dishonest client from picking any moment it likes. A claim past the
    ///     window is not rejected outright — it is served at the edge of the window, which is what an
    ///     honest client at the worst tolerated latency would have got.
    /// </summary>
    [Fact]
    public void A_rewind_deeper_than_the_window_is_pulled_back_to_its_edge()
    {
        long now = 100_000;

        Assert.Equal(
            now - EntityPositionHistory.MaxRewindMs,
            EntityPositionHistory.ClampRewind(now - 30_000, now));
    }

    /// <summary>A client claiming the future gets the present; time does not run forward for it.</summary>
    [Fact]
    public void A_rewind_into_the_future_is_pulled_back_to_now() => Assert.Equal(5000, EntityPositionHistory.ClampRewind(9000, 5000));

    /// <summary>
    ///     What the whole mechanism is for, stated as the reach check it changes.
    ///     <para>
    ///         A target walks away at Beta's sprint-less walking speed. The attacker's client renders
    ///         it 300 ms behind the server, clicks when it is 3 blocks off, and the attack reaches the
    ///         server another 100 ms later. Against the present position the target is out of reach
    ///         and the hit is rejected; against where the attacker saw it, it is not.
    ///     </para>
    /// </summary>
    [Fact]
    public void A_target_walking_away_is_still_within_reach_where_the_attacker_saw_it()
    {
        const double SpeedPerTick = 0.215; // blocks per tick, roughly Beta walking pace
        const double Reach = 4.0;

        EntityPositionHistory history = new();

        // Walking straight away, arranged so it is exactly 3 blocks off 400 ms ago — the instant the
        // attacker clicked, being 300 ms of interpolation delay plus 100 ms in flight.
        const long SeenTicksAgo = 8;

        long now = 10_000;
        for (var tick = -EntityPositionHistory.Capacity + 1; tick <= 0; tick++)
        {
            history.Record(now + tick * TickMs, 3.0 + (tick + SeenTicksAgo) * SpeedPerTick, 64.0, 0.0);
        }

        // Where the server holds the target when the attack lands: past reach.
        Assert.True(history.Sample(now, out var presentX, out _, out _));
        Assert.True(presentX > Reach, $"fixture is wrong: target is at {presentX}, still in reach");

        // Where the attacker actually saw it: 300 ms of interpolation delay plus 100 ms in flight.
        var rewind = EntityPositionHistory.ClampRewind(now - 400, now);
        Assert.True(history.Sample(rewind, out var seenX, out _, out _));
        Assert.True(seenX < Reach, $"rewound to {seenX}, which is still out of reach");
    }

    /// <summary>
    ///     Reach is the only thing the rewind moves. A target the attacker never saw within reach
    ///     stays a miss, so the compensation cannot be turned into extra range by claiming a time.
    /// </summary>
    [Fact]
    public void A_target_that_was_never_in_reach_stays_out_of_it()
    {
        const double Reach = 4.0;

        EntityPositionHistory history = new();
        long now = 10_000;

        for (var tick = -EntityPositionHistory.Capacity + 1; tick <= 0; tick++)
        {
            history.Record(now + tick * TickMs, 20.0, 64.0, 0.0);
        }

        var rewind = EntityPositionHistory.ClampRewind(now - EntityPositionHistory.MaxRewindMs, now);
        Assert.True(history.Sample(rewind, out var x, out _, out _));
        Assert.True(x > Reach);
    }

    [Fact]
    public void The_interact_message_round_trips()
    {
        InteractEntityMessage sent = new()
        {
            EntityId = -4271,
            Action = 1,
            RenderTimeMs = 1_234_567_890L
        };

        using MemoryStream buffer = new();
        sent.Write(buffer);

        Assert.Equal(sent.Size(), buffer.Length);

        buffer.Position = 0;
        InteractEntityMessage received = new();
        received.Read(buffer);

        Assert.Equal(sent.EntityId, received.EntityId);
        Assert.Equal(sent.Action, received.Action);
        Assert.Equal(sent.RenderTimeMs, received.RenderTimeMs);
    }
}
