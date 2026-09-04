namespace OmniBlock.Network;

/// <summary>
///     How a peer declares that it speaks the OmniBlock protocol, and which revision of it.
///     <para>
///         <b>Why it is smuggled inside a login field rather than sent as its own packet.</b> The
///         legacy framing has no length prefix, so an unrecognised packet ID leaves the reader unable
///         to find the next packet boundary and the connection dies. That makes an unsolicited
///         capability packet impossible in either direction: a vanilla server would die on the
///         client's, and a vanilla client would die on the server's. Declaring inside a packet the
///         other end already parses is the only move that degrades instead of disconnecting, and it
///         is why <see cref="Packets.LoginHelloPacket.WorldSeed" /> is the carrier — that field is
///         meaningless client to server, since the client cannot know the seed it is about to be
///         told.
///     </para>
///     <para>
///         The escape from this is <c>OmniMessagePacket</c>, which does length-prefix its contents.
///         Once the envelope is the framing rather than a tunnel inside it, a real handshake becomes
///         expressible and this can go.
///     </para>
/// </summary>
public static class ProtocolHandshake
{
    /// <summary>
    ///     This build's protocol revision.
    ///     <para>
    ///         Deliberately coarse and deliberately not a gate. The fine-grained compatibility unit
    ///         is <see cref="Messages.Message.SchemaVersion" />, per message, so a peer one revision
    ///         ahead loses the features that changed rather than the connection. This number exists
    ///         to make that degradation diagnosable, not to refuse anyone.
    ///     </para>
    /// </summary>
    public const int Version = 2;

    /// <summary>
    ///     "bsha". Occupies the high half of the field, leaving the low half for the revision.
    ///     <para>
    ///         A vanilla client sends zero here, so the only false positive is a client that both
    ///         sends a seed and happens to pick this exact 32-bit prefix — and a client does not send
    ///         a seed at all.
    ///     </para>
    /// </summary>
    private const long Magic = 0x6273_6861L;

    /// <summary>Encodes a declaration for <c>LoginHelloPacket.WorldSeed</c>.</summary>
    public static long Encode(int version)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        return (Magic << 32) | (uint)version;
    }

    /// <summary>
    ///     Reads a declaration back, or returns false for a peer that made none — a vanilla client,
    ///     which is not an error and must stay connectable.
    /// </summary>
    public static bool TryDecode(long value, out int version)
    {
        if (value >>> 32 != Magic)
        {
            version = 0;
            return false;
        }

        version = (int)(value & 0xFFFF_FFFFL);
        return true;
    }
}
