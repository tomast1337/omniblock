namespace BetaSharp.Network.Packets.S2CPlay;

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

    public static MessageRegistrySyncS2CPacket Get(IReadOnlyList<ResourceLocation> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keys.Count, MaxEntries);

        MessageRegistrySyncS2CPacket p = Get<MessageRegistrySyncS2CPacket>(PacketId.MessageRegistrySyncS2C);
        p.Keys = keys;
        return p;
    }

    public override void Read(Stream stream)
    {
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
        stream.WriteVarInt(Keys.Count);

        foreach (ResourceLocation key in Keys)
        {
            stream.WriteResourceLocation(key);
        }
    }

    public override void Apply(NetHandler handler) => handler.onMessageRegistrySync(this);

    public override int Size()
    {
        int size = StreamExtensions.VarIntSize(Keys.Count);

        foreach (ResourceLocation key in Keys)
        {
            // Namespace and path each go out as a length-prefixed ASCII-256 string.
            size += 1 + key.Namespace.ToString().Length + 1 + key.Path.Length;
        }

        return size;
    }
}
