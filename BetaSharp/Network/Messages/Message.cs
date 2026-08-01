namespace BetaSharp.Network.Messages;

/// <summary>
///     A message in the extensible protocol layer, identified by a <see cref="ResourceLocation" />
///     rather than by a byte from a fixed enum.
///     <para>
///         This is what <c>PacketId : byte</c> cannot be. That enum has 256 slots and no owner, so a
///         mod adding a packet has to pick a number and hope nothing else wants it. A message is
///         named — <c>omniblock:time_sync_request</c>, <c>mymod:spell_cast</c> — and the integer it
///         travels as is assigned per session by <see cref="MessageRegistry" />. There is no ID to
///         collide over and load order does not change the wire encoding.
///     </para>
///     <para>
///         Messages travel inside <c>OmniMessagePacket</c>, which length-prefixes the payload. That
///         is the second thing the legacy framing cannot do: an unknown byte ID leaves the reader
///         with no idea where the next packet starts, so the connection dies. An unknown message is
///         skipped.
///     </para>
/// </summary>
public abstract class Message
{
    /// <summary>
    ///     Stable identity, and the sort key for ID assignment. Must not vary between runs or
    ///     between client and server, since both sides derive the same ordering from it.
    /// </summary>
    public abstract ResourceLocation Key { get; }

    /// <summary>
    ///     Schema version for this message type. Bumped when the payload shape changes.
    ///     <para>
    ///         Per-type rather than one global protocol number, so a server one version ahead on a
    ///         single message degrades that one feature instead of refusing the connection. A global
    ///         version forces lockstep upgrades across an entire mod ecosystem.
    ///     </para>
    /// </summary>
    public virtual int SchemaVersion => 1;

    /// <summary>
    ///     Which send queue this message is drained from.
    ///     <para>
    ///         Declared per message rather than inherited from the envelope's packet ID, because
    ///         every message shares one ID and they do not share one urgency — a clock probe and a
    ///         mod's bulk asset transfer would otherwise be indistinguishable to the sender. This is
    ///         also the extensibility that matters: a mod says its message is latency-sensitive
    ///         instead of hunting for a packet ID that happens to be prioritised.
    ///     </para>
    ///     <para>
    ///         A local send decision, so it is not serialised. See <c>PacketPriorities</c> for what
    ///         may safely overtake bulk traffic; the same rule applies here.
    ///     </para>
    /// </summary>
    public virtual SendPriority Priority => SendPriority.Normal;

    /// <summary>
    ///     Whether the transport should stamp its send instant into the envelope on the way out.
    ///     <para>
    ///         For anything that measures the network rather than describing the world. The value
    ///         has to be taken inside the write path, immediately before the bytes reach the socket,
    ///         or the time a packet spent queued behind a chunk is counted as network latency. A
    ///         payload field cannot do that: the payload is serialised when the message is handed to
    ///         the layer, which is before the moment it would be describing.
    ///     </para>
    /// </summary>
    public virtual bool NeedsSendTimestamp => false;

    /// <summary>
    ///     Transport send instant, from the envelope, or 0 when this message did not ask for one.
    ///     See <see cref="NeedsSendTimestamp" />.
    /// </summary>
    public long TransportSentAtMs { get; internal set; }

    /// <summary>
    ///     Transport arrival instant, taken on the read thread before the packet was queued.
    ///     <para>
    ///         Always present, because it costs one clock reading the read loop already takes. It is
    ///         not the same as "when the handler saw it": handlers run on the game thread up to a
    ///         full tick later, so a timestamp taken there measures the tick phase rather than the
    ///         network. Never serialised — it describes this peer's own receipt.
    ///     </para>
    /// </summary>
    public long TransportReceivedAtMs { get; internal set; }

    public abstract void Read(Stream stream);

    public abstract void Write(Stream stream);

    /// <summary>Serialised byte count, excluding the envelope's ID and length prefix.</summary>
    public abstract int Size();
}
