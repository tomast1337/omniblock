using OmniBlock.Network.Messages;
using OmniBlock.Network.Snapshots;

namespace OmniBlock.Tests.Network;

/// <summary>
///     Delta-compressed entity replication: <see cref="EntitySnapshotMessage" />,
///     <see cref="PlayerSnapshotStream" /> and <see cref="ClientSnapshotStream" /> as one loop.
///     <para>
///         Delta compression fails quietly — a snapshot applied to the wrong baseline decodes to a
///         plausible position rather than to an error — so most of
///         these drive a real sender against a real receiver and assert the receiver reconstructed
///         what the sender put in, rather than testing either half against a fixture.
///     </para>
/// </summary>
public sealed class EntitySnapshotTests
{
    private static List<KeyValuePair<int, EntitySnapshotState>> Pass(
        params (int Id, EntitySnapshotState State)[] entries) =>
        [.. entries.Select(e => new KeyValuePair<int, EntitySnapshotState>(e.Id, e.State))];

    private static EntitySnapshotState At(int x, int y, int z, byte yaw = 0, byte pitch = 0) =>
        new(x, y, z, yaw, pitch);

    /// <summary>Serialises and reparses, so decode is tested against bytes rather than against the object.</summary>
    private static EntitySnapshotMessage RoundTrip(EntitySnapshotMessage message)
    {
        using MemoryStream buffer = new();
        message.Write(buffer);

        Assert.Equal(message.Size(), buffer.Length);

        buffer.Position = 0;
        EntitySnapshotMessage received = new();
        received.Read(buffer);
        return received;
    }

    [Fact]
    public void The_first_snapshot_has_no_baseline_and_carries_absolute_positions()
    {
        PlayerSnapshotStream server = new();

        EntitySnapshotMessage? snapshot = server.Build(Pass((7, At(320, 2048, -64, 12, 3))));

        Assert.NotNull(snapshot);
        Assert.Equal(0u, snapshot.Baseline);
        Assert.Equal(1u, snapshot.Sequence);

        EntitySnapshotMessage.EntityDelta delta = Assert.Single(snapshot.Deltas);
        Assert.True((delta.Mask & EntitySnapshotMessage.Field.Absolute) != 0);
        Assert.Equal(320, delta.X);
        Assert.Equal(2048, delta.Y);
        Assert.Equal(-64, delta.Z);
    }

    /// <summary>
    ///     The compression itself: a coordinate that did not change is not on the wire at all. The
    ///     packets this replaces send all three whenever any of them moves.
    /// </summary>
    [Fact]
    public void Only_the_fields_that_changed_are_sent()
    {
        PlayerSnapshotStream server = new();
        ClientSnapshotStream client = new();

        client.Apply(RoundTrip(server.Build(Pass((7, At(320, 2048, -64))))!));
        server.Acknowledge(client.AppliedSequence);

        EntitySnapshotMessage second = server.Build(Pass((7, At(321, 2048, -64))))!;

        EntitySnapshotMessage.EntityDelta delta = Assert.Single(second.Deltas);
        Assert.Equal(EntitySnapshotMessage.Field.X, delta.Mask);
        Assert.Equal(1, delta.X);
    }

    /// <summary>An entity that did not move at all produces no record, and a pass of them produces no message.</summary>
    [Fact]
    public void A_pass_in_which_nothing_changed_sends_nothing()
    {
        PlayerSnapshotStream server = new();
        ClientSnapshotStream client = new();

        client.Apply(RoundTrip(server.Build(Pass((7, At(320, 2048, -64))))!));
        server.Acknowledge(client.AppliedSequence);

        Assert.Null(server.Build(Pass((7, At(320, 2048, -64)))));
    }

    /// <summary>
    ///     The end-to-end property, over a run of ticks: whatever the server put in comes out the
    ///     other side, byte-exactly, through serialisation and the delta chain.
    /// </summary>
    [Fact]
    public void A_run_of_snapshots_reconstructs_every_position_exactly()
    {
        PlayerSnapshotStream server = new();
        ClientSnapshotStream client = new();

        Dictionary<int, EntitySnapshotState> truth = [];
        Dictionary<int, EntitySnapshotState> received = [];

        for (int tick = 0; tick < 200; tick++)
        {
            List<KeyValuePair<int, EntitySnapshotState>> pass = [];

            for (int entity = 1; entity <= 12; entity++)
            {
                // Deliberately uneven: some entities stand still, some drift, some spin.
                EntitySnapshotState state = At(
                    (tick * entity) % 97,
                    2048 + (entity % 3 == 0 ? 0 : tick % 5),
                    -(tick % 31) * entity,
                    (byte)((tick * entity) % 256),
                    (byte)(entity % 2 == 0 ? 0 : tick % 256));

                truth[entity] = state;
                pass.Add(new KeyValuePair<int, EntitySnapshotState>(entity, state));
            }

            EntitySnapshotMessage? snapshot = server.Build(pass);
            if (snapshot is null)
            {
                continue;
            }

            foreach ((int id, EntitySnapshotState state) in client.Apply(RoundTrip(snapshot)))
            {
                received[id] = state;
            }

            server.Acknowledge(client.AppliedSequence);
        }

        Assert.Equal(truth, received);
    }

    /// <summary>
    ///     Loss recovery without a resynchronisation. A snapshot never applied leaves the client
    ///     acknowledging an older sequence, and the server measures against that instead — the whole
    ///     reason the baseline is named rather than implied. This is what the packets it replaces
    ///     cannot do, and why they need a reliable ordered channel.
    /// </summary>
    [Fact]
    public void A_lost_snapshot_costs_one_redundant_delta_and_nothing_else()
    {
        PlayerSnapshotStream server = new();
        ClientSnapshotStream client = new();

        client.Apply(RoundTrip(server.Build(Pass((7, At(0, 2048, 0))))!));
        server.Acknowledge(client.AppliedSequence);

        // Sent, and lost in transit: built, never applied, never acknowledged.
        EntitySnapshotMessage lost = server.Build(Pass((7, At(32, 2048, 0))))!;
        Assert.NotNull(lost);

        EntitySnapshotMessage next = server.Build(Pass((7, At(64, 2048, 0))))!;

        // Measured against sequence 1, the newest the client confirmed — not against the lost one.
        Assert.Equal(1u, next.Baseline);

        EntitySnapshotState state = Assert.Single(client.Apply(RoundTrip(next))).Value;
        Assert.Equal(At(64, 2048, 0), state);
        Assert.Equal(0, client.DroppedSnapshots);
    }

    /// <summary>
    ///     The receiver's half of the same property: a snapshot whose baseline it cannot reconstruct
    ///     is refused whole rather than applied against the wrong state, which would put the entity
    ///     somewhere plausible and wrong.
    /// </summary>
    [Fact]
    public void A_snapshot_naming_an_unknown_baseline_is_dropped_rather_than_misapplied()
    {
        ClientSnapshotStream client = new();

        EntitySnapshotMessage stray = new()
        {
            Sequence = 900,
            Baseline = 899,
            Deltas = [new EntitySnapshotMessage.EntityDelta(7, EntitySnapshotMessage.Field.X, 5, 0, 0, 0, 0)],
        };

        Assert.Empty(client.Apply(stray));
        Assert.Equal(0u, client.AppliedSequence);
        Assert.Equal(1, client.DroppedSnapshots);
    }

    /// <summary>
    ///     A peer that stops acknowledging must not grow the sender's staging queue without limit.
    ///     The stream gives up and resynchronises, which costs one absolute snapshot.
    /// </summary>
    [Fact]
    public void A_peer_that_never_acknowledges_is_resynchronised_rather_than_queued_forever()
    {
        PlayerSnapshotStream server = new();

        for (int tick = 0; tick < SnapshotBaseline.MaxStagedSnapshots * 2; tick++)
        {
            EntitySnapshotMessage? snapshot = server.Build(Pass((7, At(tick, 2048, 0))));
            Assert.NotNull(snapshot);
            Assert.True(
                server.InFlight <= SnapshotBaseline.MaxStagedSnapshots,
                $"{server.InFlight} snapshots in flight past the {SnapshotBaseline.MaxStagedSnapshots} bound");
        }

        // Having reset, it is speaking absolutely again — which is decodable by a client that also
        // gave up, and is the only state the two can agree on without one.
        EntitySnapshotMessage last = server.Build(Pass((7, At(9999, 2048, 0))))!;
        Assert.Equal(0u, last.Baseline);
    }

    /// <summary>
    ///     An entity that leaves and returns must not be delta'd against where it was before it left.
    ///     Both ends drop it on the same event, so this asserts the sender re-sends it absolute.
    /// </summary>
    [Fact]
    public void An_entity_that_left_view_is_sent_in_full_when_it_returns()
    {
        PlayerSnapshotStream server = new();
        ClientSnapshotStream client = new();

        client.Apply(RoundTrip(server.Build(Pass((7, At(320, 2048, -64))))!));
        server.Acknowledge(client.AppliedSequence);

        server.Forget(7);
        client.Forget(7);

        EntitySnapshotMessage returned = server.Build(Pass((7, At(1024, 2048, 512))))!;
        EntitySnapshotMessage.EntityDelta delta = Assert.Single(returned.Deltas);
        Assert.True((delta.Mask & EntitySnapshotMessage.Field.Absolute) != 0);

        Assert.Equal(At(1024, 2048, 512), Assert.Single(client.Apply(RoundTrip(returned))).Value);
    }

    /// <summary>
    ///     Forgetting must reach the staged changes too, or the entity reappears in the baseline the
    ///     next time it advances and the two ends disagree about whether it is present.
    /// </summary>
    [Fact]
    public void Forgetting_an_entity_survives_the_baseline_advancing_past_it()
    {
        SnapshotBaseline baseline = new();
        baseline.Stage(1, [new KeyValuePair<int, EntitySnapshotState>(7, At(1, 2, 3))]);
        baseline.Forget(7);

        Assert.True(baseline.AdvanceTo(1));
        Assert.False(baseline.TryGet(7, out _));
    }

    /// <summary>
    ///     A client that has given up asks for a resynchronisation by acknowledging nothing, and the
    ///     server has to honour it — otherwise the two sit at different baselines forever, decoding
    ///     deltas against states that do not match.
    /// </summary>
    [Fact]
    public void Acknowledging_zero_resets_the_sender()
    {
        PlayerSnapshotStream server = new();
        ClientSnapshotStream client = new();

        client.Apply(RoundTrip(server.Build(Pass((7, At(320, 2048, -64))))!));
        server.Acknowledge(client.AppliedSequence);
        Assert.NotEqual(0u, server.BaselineSequence);

        server.Acknowledge(0);
        Assert.Equal(0u, server.BaselineSequence);

        EntitySnapshotMessage resynchronised = server.Build(Pass((7, At(320, 2048, -64))))!;
        Assert.Equal(0u, resynchronised.Baseline);
        Assert.True(
            (Assert.Single(resynchronised.Deltas).Mask & EntitySnapshotMessage.Field.Absolute) != 0);
    }

    /// <summary>
    ///     An acknowledgement that arrives out of order carries nothing the newer one did not, and
    ///     must not roll the baseline backwards into re-sending state the peer already has.
    /// </summary>
    [Fact]
    public void A_stale_acknowledgement_does_not_move_the_baseline_backwards()
    {
        PlayerSnapshotStream server = new();

        server.Build(Pass((7, At(0, 2048, 0))));
        server.Build(Pass((7, At(32, 2048, 0))));

        server.Acknowledge(2);
        Assert.Equal(2u, server.BaselineSequence);

        server.Acknowledge(1);
        Assert.Equal(2u, server.BaselineSequence);
    }

    /// <summary>
    ///     A record must survive serialisation exactly. Rotation is a raw byte and position is a
    ///     zig-zagged varint, so this is also the test that a negative delta does not become a
    ///     five-byte positive one.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, -1, 2)]
    [InlineData(-4096, 4096, -1)]
    [InlineData(int.MinValue, int.MaxValue, 0)]
    public void Position_deltas_round_trip_through_the_wire(int x, int y, int z)
    {
        EntitySnapshotMessage message = new()
        {
            Sequence = 42,
            Baseline = 41,
            Deltas =
            [
                new EntitySnapshotMessage.EntityDelta(
                    -12345, EntitySnapshotMessage.Field.All, x, y, z, 200, 55),
            ],
        };

        EntitySnapshotMessage received = RoundTrip(message);

        Assert.Equal(42u, received.Sequence);
        Assert.Equal(41u, received.Baseline);

        EntitySnapshotMessage.EntityDelta delta = Assert.Single(received.Deltas);
        Assert.Equal(-12345, delta.EntityId);
        Assert.Equal(x, delta.X);
        Assert.Equal(y, delta.Y);
        Assert.Equal(z, delta.Z);
        Assert.Equal(200, delta.Yaw);
        Assert.Equal(55, delta.Pitch);
    }

    [Fact]
    public void A_declared_record_count_past_the_bound_is_refused()
    {
        using MemoryStream buffer = new();
        buffer.WriteVarInt(1);
        buffer.WriteVarInt(0);
        buffer.WriteVarInt(EntitySnapshotMessage.MaxRecords + 1);
        buffer.Position = 0;

        Assert.Throws<InvalidDataException>(() => new EntitySnapshotMessage().Read(buffer));
    }

    /// <summary>
    ///     The size claim, against the four packets this replaces. Two hundred entities each moving a
    ///     little is a busy tick at a normal view distance; the packets cost an entity ID and their
    ///     own framing apiece, and the snapshot costs an ID gap and a mask.
    /// </summary>
    [Fact]
    public void A_busy_tick_costs_less_than_the_packets_it_replaces()
    {
        PlayerSnapshotStream server = new();
        ClientSnapshotStream client = new();

        List<KeyValuePair<int, EntitySnapshotState>> first = [];
        for (int entity = 1; entity <= 200; entity++)
        {
            first.Add(new KeyValuePair<int, EntitySnapshotState>(entity, At(entity * 32, 2048, entity * 16, 64, 0)));
        }

        client.Apply(server.Build(first)!);
        server.Acknowledge(client.AppliedSequence);

        List<KeyValuePair<int, EntitySnapshotState>> second = [];
        for (int entity = 1; entity <= 200; entity++)
        {
            // Everything shuffles along by a fraction of a block and turns slightly, which is what a
            // tick of a populated area looks like.
            second.Add(new KeyValuePair<int, EntitySnapshotState>(
                entity, At((entity * 32) + 2, 2048, (entity * 16) - 1, 66, 0)));
        }

        int snapshotBytes = server.Build(second)!.Size();

        // EntityRotateAndMoveRelativeS2CPacket: one byte of ID, four of entity ID, three of position
        // delta, two of rotation. The comparison is per entity because that is how it is sent.
        const int LegacyBytesPerEntity = 10;
        int legacyBytes = 200 * LegacyBytesPerEntity;

        Assert.True(
            snapshotBytes < legacyBytes,
            $"{snapshotBytes} bytes of snapshot against {legacyBytes} of packets");
    }
}
