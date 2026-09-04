using OmniBlock.Util.Maths;

namespace OmniBlock.Network.Messages;

/// <summary>
///     What chunks the client already holds, and the content hash of each.
///     <para>
///         The server compares each hash against the chunk it was about to send and, on a match,
///         sends <see cref="ChunkUnchangedMessage" />
///         instead of two kilobytes. Rejoining a world you have explored is the case this exists for,
///         and it takes that from a full re-send to nothing.
///     </para>
///     <para>
///         <b>The client advertises rather than the server asking.</b> The alternative — server sends
///         a hash, client answers whether it needs the chunk — costs a round trip per chunk, which at
///         a few hundred chunks per join is far worse than the traffic it saves. Advertising up front
///         costs one message and no round trips.
///     </para>
///     <para>
///         <b>A stale or wrong-world cache is safe, not merely tolerable.</b> The server compares
///         against its own chunk's hash, so an entry that does not match simply loses and the chunk is
///         sent in full. Nothing here is trusted: the worst a client can do by lying is decline data
///         it then does not have, which corrupts only its own view.
///     </para>
/// </summary>
public sealed class ChunkCacheOfferMessage : Message
{
    /// <summary>
    ///     Bounds what one offer can cost, in upstream bytes and in server memory. At sixteen bytes
    ///     an entry this is half a megabyte, which already covers a radius far past any view distance
    ///     — the offer is sized to where the player is, not to everything they have ever seen.
    /// </summary>
    public const int MaxEntries = 32_768;

    public static readonly ResourceLocation Id = new(Namespace.OmniBlock, "chunk_cache_offer");

    public override ResourceLocation Key => Id;

    /// <summary>Chunk position to the hash the client holds for it.</summary>
    public List<KeyValuePair<ChunkPos, ulong>> Entries { get; } = [];

    public override void Read(Stream stream)
    {
        Entries.Clear();

        var count = stream.ReadInt();
        if (count is < 0 or > MaxEntries)
        {
            throw new InvalidDataException($"Chunk cache offer declares {count} entries; the limit is {MaxEntries}.");
        }

        Entries.Capacity = count;
        for (var i = 0; i < count; i++)
        {
            var x = stream.ReadInt();
            var z = stream.ReadInt();
            var hash = (ulong)stream.ReadLong();

            Entries.Add(new KeyValuePair<ChunkPos, ulong>(new ChunkPos(x, z), hash));
        }
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(Entries.Count);

        foreach (var (position, hash) in Entries)
        {
            stream.WriteInt(position.X);
            stream.WriteInt(position.Z);
            stream.WriteLong((long)hash);
        }
    }

    public override int Size() => sizeof(int) + Entries.Count * (sizeof(int) * 2 + sizeof(long));
}
