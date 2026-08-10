namespace OmniBlock.Network.Packets;

/// <summary>
///     Advertises the server's message table so both peers agree on which integer means which
///     message key, for this session only.
///     <para>
///         Sent once, during configuration, before any <see cref="OmniMessagePacket" />. It cannot
///         itself be a message, since it is what establishes how messages are identified — hence a
///         legacy packet ID for the bootstrap and name-keyed IDs for everything after.
///     </para>
///     <para>
///         The server's ordering wins outright, and there is no reply. A client that does not know
///         an advertised key leaves a hole in its table and skips those messages; a client that
///         registered a key the server never advertised simply never sends it. Both are correct
///         outcomes for a version or mod-set mismatch, and neither needs a negotiation round trip.
///     </para>
/// </summary>
public class MessageRegistrySyncS2CPacket() : ExtendedProtocolPacket(PacketId.MessageRegistrySyncS2C)
{
    /// <summary>
    ///     Refuses a table larger than this. The count is peer-supplied and drives a loop that
    ///     allocates, so it is bounded rather than trusted.
    /// </summary>
    public const int MaxEntries = 4096;

    /// <summary>Message keys in wire-ID order: index 0 is ID 0.</summary>
    public IReadOnlyList<ResourceLocation> Keys { get; private set; } = [];

    /// <summary>
    ///     The server's protocol revision. Here because this is the first extended packet the server
    ///     sends, so it is the earliest point the client can be told, and because a packet that
    ///     already exists to negotiate capability is the honest place for it.
    ///     <para>
    ///         The client's own declaration travels the other way inside the login field; see
    ///         <see cref="ProtocolHandshake" />. This is the half that closes the loop, and without
    ///         it the client could only ever infer that the server is capable, never which revision
    ///         it is talking to.
    ///     </para>
    /// </summary>
    public int ProtocolVersion { get; private set; }

    public static MessageRegistrySyncS2CPacket Get(IReadOnlyList<ResourceLocation> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keys.Count, MaxEntries);

        MessageRegistrySyncS2CPacket p = Get<MessageRegistrySyncS2CPacket>(PacketId.MessageRegistrySyncS2C);
        p.Keys = keys;
        p.ProtocolVersion = ProtocolHandshake.Version;
        return p;
    }

    public override void Read(Stream stream)
    {
        ProtocolVersion = stream.ReadVarInt();

        int count = stream.ReadVarInt();
        if (count < 0 || count > MaxEntries)
        {
            throw new InvalidDataException(
                $"Message table declares {count} entries; the limit is {MaxEntries}.");
        }

        ResourceLocation[] keys = new ResourceLocation[count];
        for (int i = 0; i < count; i++)
        {
            keys[i] = stream.ReadResourceLocation();
        }

        Keys = keys;
    }

    public override void Write(Stream stream)
    {
        stream.WriteVarInt(ProtocolVersion);
        stream.WriteVarInt(Keys.Count);

        foreach (ResourceLocation key in Keys)
        {
            stream.WriteResourceLocation(key);
        }
    }

    public override void Apply(NetHandler handler) => handler.onMessageRegistrySync(this);

    public override int Size()
    {
        int size = StreamExtensions.VarIntSize(ProtocolVersion) + StreamExtensions.VarIntSize(Keys.Count);

        foreach (ResourceLocation key in Keys)
        {
            // Path always goes out as a length-prefixed ASCII-256 string. The namespace does
            // too, except WriteNamespace takes a 1-byte fast path for the default namespace.
            int namespaceSize = key.Namespace.GetHashCode() == 0 ? 1 : 1 + key.Namespace.ToString().Length;
            size += namespaceSize + 1 + key.Path.Length;
        }

        return size;
    }
}
