using BetaSharp.Network;
using BetaSharp.Network.Packets;

namespace BetaSharp.Tests.Network;

/// <summary>
///     Phase 6 of <c>docs/time-sync-and-interpolation.md</c> (§4.1). The priority split is the one
///     change in this subsystem that can reorder the wire, so the tests here are as much about what
///     must <em>not</em> jump the queue as about what must.
/// </summary>
public sealed class SendPriorityTests
{
    /// <summary>
    ///     A <see cref="Connection" /> with no socket. The parameterless constructor is the one
    ///     <c>InternalConnection</c> uses; it starts no read or write thread, which is what makes
    ///     the queues observable without a network.
    /// </summary>
    private sealed class QueueOnlyConnection : Connection
    {
        public QueueOnlyConnection()
        {
            // Otherwise sendPacket's compatibility gate discards every ExtendedProtocolPacket, and
            // TickStamp — one of the packets the priority queue exists for — never reaches a queue.
            betaSharpClient = true;
        }

        public List<Packet> DrainAll()
        {
            List<Packet> drained = [];
            while (TryDequeueNext(out Packet? packet))
            {
                drained.Add(packet!);
            }

            return drained;
        }
    }

    [Fact]
    public void Entity_and_timing_packets_are_high_priority()
    {
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.EntityMoveRelativeS2C)));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.EntityPositionS2C)));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.EntityDestroyS2C)));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.LivingEntitySpawnS2C)));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.TickStamp)));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.TimeSyncResponse)));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.KeepAlive)));
    }

    /// <summary>
    ///     The safety property. A block update that overtakes the chunk it edits is applied to a
    ///     chunk the client does not have and is lost, and a server-sent player position that
    ///     overtakes the login chunk batch places the player in unloaded terrain. Neither may be
    ///     reordered against bulk, so both stay normal.
    /// </summary>
    [Fact]
    public void World_data_and_anything_ordered_against_it_stays_normal()
    {
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChunkDataS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.BlockUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChunkDeltaUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChunkStatusUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.PlayerMoveFull)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.MapUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChatMessage)));
    }

    [Fact]
    public void High_priority_packets_are_drained_before_bulk_regardless_of_send_order()
    {
        QueueOnlyConnection connection = new();

        connection.sendPacket(Packet.Get(PacketId.ChunkDataS2C));
        connection.sendPacket(Packet.Get(PacketId.EntityMoveRelativeS2C));
        connection.sendPacket(Packet.Get(PacketId.BlockUpdateS2C));
        connection.sendPacket(Packet.Get(PacketId.TickStamp));

        byte[] order = [.. connection.DrainAll().Select(p => p.Id)];

        Assert.Equal(
            [
                (byte)PacketId.EntityMoveRelativeS2C,
                (byte)PacketId.TickStamp,
                (byte)PacketId.ChunkDataS2C,
                (byte)PacketId.BlockUpdateS2C,
            ],
            order);
    }

    /// <summary>
    ///     Reordering happens only across the two classes. Within either one, order is exactly what
    ///     it was — which is what keeps a move from overtaking its own spawn.
    /// </summary>
    [Fact]
    public void Order_within_each_class_is_preserved()
    {
        QueueOnlyConnection connection = new();

        connection.sendPacket(Packet.Get(PacketId.LivingEntitySpawnS2C));
        connection.sendPacket(Packet.Get(PacketId.ChunkDataS2C));
        connection.sendPacket(Packet.Get(PacketId.EntityMoveRelativeS2C));
        connection.sendPacket(Packet.Get(PacketId.BlockUpdateS2C));
        connection.sendPacket(Packet.Get(PacketId.EntityDestroyS2C));

        byte[] order = [.. connection.DrainAll().Select(p => p.Id)];

        Assert.Equal(
            [
                (byte)PacketId.LivingEntitySpawnS2C,
                (byte)PacketId.EntityMoveRelativeS2C,
                (byte)PacketId.EntityDestroyS2C,
                (byte)PacketId.ChunkDataS2C,
                (byte)PacketId.BlockUpdateS2C,
            ],
            order);
    }

    /// <summary>
    ///     The depth counters feed the overflow disconnect and the F3 overlay, so a packet parked in
    ///     the priority queue has to count toward the total or a backlog there is invisible.
    /// </summary>
    [Fact]
    public void Queue_depth_counts_both_queues()
    {
        QueueOnlyConnection connection = new();

        connection.sendPacket(Packet.Get(PacketId.ChunkDataS2C));
        connection.sendPacket(Packet.Get(PacketId.EntityMoveRelativeS2C));
        connection.sendPacket(Packet.Get(PacketId.TickStamp));

        Assert.Equal(3, connection.SendQueueDepth);
        Assert.Equal(2, connection.PrioritySendQueueDepth);
    }
}
