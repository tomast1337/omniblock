namespace BetaSharp.Network.Packets;

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

    /// <summary>Negotiated wire ID, meaningful only against the session's <c>MessageRegistry</c>.</summary>
    public int MessageId { get; private set; }

    public byte[] Payload { get; private set; } = [];

    public static OmniMessagePacket Get(int messageId, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfNegative(messageId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payload.Length, MaxPayloadBytes);

        OmniMessagePacket p = Get<OmniMessagePacket>(PacketId.OmniMessage);
        p.MessageId = messageId;
        p.Payload = payload;
        return p;
    }

    public override void Read(Stream stream)
    {
        MessageId = stream.ReadVarInt();

        int length = stream.ReadVarInt();
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
        stream.WriteVarInt(Payload.Length);
        stream.Write(Payload);
    }

    /// <summary>
    ///     Decoding needs the session's negotiated table, which <c>Packet.Read</c> has no access to,
    ///     so the payload is carried raw to the handler and resolved there.
    /// </summary>
    public override void Apply(NetHandler handler) => handler.onOmniMessage(this);

    public override int Size() =>
        StreamExtensions.VarIntSize(MessageId) + StreamExtensions.VarIntSize(Payload.Length) + Payload.Length;
}
