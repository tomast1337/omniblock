using OmniBlock.Network.Messages;

namespace OmniBlock.Network.Packets;

/// <summary>
///     Carries one extensible-layer message inside the legacy byte-ID framing.
///     <para>
///         Tunnelling rather than replacing the framing is what makes this landable incrementally.
///         The stream stays exactly as it is; legacy packets keep working untouched; and because
///         this is an <see cref="ExtendedProtocolPacket" />, <c>Connection.sendPacket</c> already
///         drops it for clients that did not identify as OmniBlock. There is no flag day and no
///         moment where both peers must switch encoding at the same byte offset. When every packet
///         has migrated, the outer ID disappears and the envelope becomes the framing.
///     </para>
///     <para>
///         The payload is length-prefixed, which is the point of the exercise. An unrecognised
///         message is skipped with a diagnostic instead of desynchronising the stream — the legacy
///         framing cannot do that, because without a length an unknown ID leaves the reader unable
///         to find where the next packet begins.
///     </para>
/// </summary>
public class OmniMessagePacket() : ExtendedProtocolPacket(PacketId.OmniMessage)
{
    /// <summary>
    ///     Refuses a payload larger than this. <c>Read</c> allocates the buffer from a length the
    ///     peer supplies, so an unbounded value is a one-packet out-of-memory attack. Sized well
    ///     above any plausible message; bulk transfers belong in their own packet, not in here.
    /// </summary>
    public const int MaxPayloadBytes = 2 * 1024 * 1024;

    /// <summary>Set when the envelope carries a transport send instant.</summary>
    private const byte SendTimestampFlag = 1;

    /// <summary>Negotiated wire ID, meaningful only against the session's <c>MessageRegistry</c>.</summary>
    public int MessageId { get; private set; }

    public byte[] Payload { get; private set; } = [];

    /// <summary>
    ///     Whether this envelope reserves room for a send instant. Fixed when the envelope is
    ///     built, so <see cref="Size" /> stays stable while the value itself is filled in later.
    /// </summary>
    public bool CarriesSendTime { get; private set; }

    /// <summary>
    ///     The wrapped message's declared priority, so <c>PacketPriorities</c> can route this
    ///     envelope without decoding it. Not serialised: it is a local decision about this peer's
    ///     own send queue, and the receiver has no use for it.
    /// </summary>
    public SendPriority Priority { get; private set; } = SendPriority.Normal;

    /// <summary>
    ///     The transport's send instant, filled in by <c>Connection.WritePacket</c> immediately
    ///     before the bytes reach the socket. Zero when <see cref="CarriesSendTime" /> is false.
    ///     <para>
    ///         Here rather than in the payload because the payload is already serialised by the time
    ///         anything knows when it will be sent, and a queued message can wait behind a chunk for
    ///         longer than the round trip it is trying to measure.
    ///     </para>
    /// </summary>
    public long SentAtMs { get; internal set; }

    /// <summary>
    ///     Arrival instant, stamped on the read thread before this was queued. Never serialised —
    ///     it describes this peer's own receipt, so there is nothing to transmit.
    /// </summary>
    public long ReceivedAtMs { get; internal set; }

    /// <summary>
    ///     The message itself, when this envelope never has to become bytes.
    ///     <para>
    ///         Loopback hands packets over as objects, which is the property that makes singleplayer
    ///         free — and migrating a packet to a message would have taken it away, because
    ///         <see cref="For" /> serialises unconditionally. At a few clicks a tick that is
    ///         invisible; at one message per tracked entity per tick it is a serialise and a matching
    ///         parse of the whole entity set, on a connection with no wire.
    ///     </para>
    ///     <para>
    ///         Null on any real connection, where the payload is the only thing that exists.
    ///     </para>
    /// </summary>
    public Message? Carried { get; private set; }

    /// <summary>
    ///     Wraps a message for a connection that will never serialise it. The registry is not
    ///     consulted: there is no ID to assign because no ID is ever written.
    /// </summary>
    public static OmniMessagePacket Loopback(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var p = Get<OmniMessagePacket>(PacketId.OmniMessage);
        p.MessageId = -1;
        p.Payload = [];
        p.CarriesSendTime = false;
        p.Priority = message.Priority;
        p.SentAtMs = 0;
        p.ReceivedAtMs = 0;
        p.Carried = message;
        return p;
    }

    public static OmniMessagePacket Get(
        int messageId,
        byte[] payload,
        bool carriesSendTime = false,
        SendPriority priority = SendPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfNegative(messageId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payload.Length, MaxPayloadBytes);

        var p = Get<OmniMessagePacket>(PacketId.OmniMessage);
        p.MessageId = messageId;
        p.Payload = payload;
        p.CarriesSendTime = carriesSendTime;
        p.Priority = priority;
        p.SentAtMs = 0;
        p.ReceivedAtMs = 0;
        p.Carried = null;
        return p;
    }

    /// <summary>
    ///     Wraps a message for the session's negotiated table, or returns null when the peer never
    ///     advertised the key. Null is the normal answer for a message the peer does not implement,
    ///     not an error — it is the per-type degradation the layer exists to allow.
    /// </summary>
    public static OmniMessagePacket? For(MessageRegistry registry, Message message)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(message);

        var id = registry.GetId(message.Key);
        if (id < 0)
        {
            return null;
        }

        using MemoryStream buffer = new(message.Size());
        message.Write(buffer);

        return Get(id, buffer.ToArray(), message.NeedsSendTimestamp, message.Priority);
    }

    public override void Read(Stream stream)
    {
        MessageId = stream.ReadVarInt();

        var flags = (byte)stream.ReadByte();
        CarriesSendTime = (flags & SendTimestampFlag) != 0;
        SentAtMs = CarriesSendTime ? stream.ReadLong() : 0;

        var length = stream.ReadVarInt();
        if (length < 0 || length > MaxPayloadBytes)
        {
            throw new InvalidDataException(
                $"OmniMessage declares a {length} byte payload; the limit is {MaxPayloadBytes}.");
        }

        Payload = new byte[length];
        stream.ReadExactly(Payload);
    }

    public override void Write(Stream stream)
    {
        stream.WriteVarInt(MessageId);

        stream.WriteByte(CarriesSendTime ? SendTimestampFlag : (byte)0);
        if (CarriesSendTime)
        {
            stream.WriteLong(SentAtMs);
        }

        stream.WriteVarInt(Payload.Length);
        stream.Write(Payload);
    }

    /// <summary>
    ///     Decoding needs the session's negotiated table, which <c>Packet.Read</c> has no access to,
    ///     so the payload is carried raw to the handler and resolved there.
    /// </summary>
    public override void Apply(NetHandler handler) => handler.onOmniMessage(this);

    public override int Size() =>
        Carried is not null
            ? Carried.Size()
            : StreamExtensions.VarIntSize(MessageId)
              + sizeof(byte)
              + (CarriesSendTime ? sizeof(long) : 0)
              + StreamExtensions.VarIntSize(Payload.Length)
              + Payload.Length;
}
