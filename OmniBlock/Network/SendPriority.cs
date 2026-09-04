using OmniBlock.Network.Packets;

namespace OmniBlock.Network;

/// <summary>
///     Which of <see cref="Connection" />'s two send queues a packet is drained from.
/// </summary>
public enum SendPriority
{
    /// <summary>Strict FIFO against every other <see cref="Normal" /> packet. The default.</summary>
    Normal,

    /// <summary>Drained ahead of anything <see cref="Normal" />. See <see cref="PacketPriorities" />.</summary>
    High
}

/// <summary>
///     Decides which packets may overtake bulk traffic on the send queue.
///     <para>
///         A chunk is ~81 KB before compression and the writer drains strictly in order, so once
///         one is being written every entity update behind it waits for all of it. That is the
///         stall the snapshot buffer then has to absorb; draining a second queue first shrinks it
///         rather than hiding it.
///     </para>
///     <para>
///         <b>An allowlist, deliberately, rather than "everything except chunks".</b> Reordering is
///         only safe for packets whose meaning does not depend on world data having arrived first.
///         A block update that overtakes the chunk it edits is applied to a chunk the client does
///         not have and is silently lost, and a server-sent player position that overtakes the login
///         chunk batch places the player in unloaded terrain. Both stay <see cref="SendPriority.Normal" />,
///         so their order relative to chunk data is exactly what it was.
///     </para>
///     <para>
///         What is on the list is entity replication and timing. Entity packets keep their order
///         relative to <em>each other</em> because they share one queue, so a move never overtakes
///         its own spawn; and a spawn arriving before its chunk is a case the client already
///         handles, by parking the entity in <c>ClientWorld.pendingEntities</c> until the chunk
///         loads.
///     </para>
/// </summary>
public static class PacketPriorities
{
    /// <summary>
    ///     Strict priority — high drains until empty — rather than a weighted share. Safe because
    ///     the high set is bounded by tick rate and tracked-entity count, a few KB/s, while bulk
    ///     traffic is elastic and takes whatever is left. There is no rate at which entity
    ///     replication starves chunk streaming outright.
    /// </summary>
    public static SendPriority Of(Packet packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        // Every extensible-layer message shares one packet ID, so the ID cannot say how urgent one
        // is. The message declares it and the envelope carries the answer, which is what stops a
        // mod's bulk transfer and a clock probe from being indistinguishable here.
        if (packet is OmniMessagePacket envelope)
        {
            return envelope.Priority;
        }

        // Everything that was manually listed here — entity replication, spawns, keep-alive — has
        // migrated to the message layer, so the switch that used to follow is gone. What is left to
        // reach this line is the login handshake and the table advertisement, none of which competes
        // with anything: they are the only packets that travel before the message layer exists.
        return SendPriority.Normal;
    }
}
