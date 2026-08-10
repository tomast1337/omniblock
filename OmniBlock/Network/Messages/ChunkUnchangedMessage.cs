namespace OmniBlock.Network.Messages;

/// <summary>
///     Tells the client the chunk it offered a hash for is still current, so it should load its own
///     copy rather than wait for one.
///     <para>
///         The payoff half of <see cref="ChunkCacheOfferMessage" />: eight bytes instead of the two
///         kilobytes <see cref="ChunkDataMessage" /> would have cost.
///     </para>
///     <para>
///         No hash on the wire. The server only sends this in reply to a hash the client itself
///         advertised, so repeating it would be telling the client something it just said. If the
///         client no longer has the chunk by the time this arrives — an eviction between the offer
///         and the send — it asks for the chunk again rather than guessing.
///     </para>
/// </summary>
public sealed class ChunkUnchangedMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "chunk_unchanged");

    /// <summary>
    ///     Bulk, matching <see cref="ChunkDataMessage" />. It is small, but it is the same traffic
    ///     class and reordering it ahead of a chunk it replaces would gain nothing.
    /// </summary>
    public override SendPriority Priority => SendPriority.Normal;

    public int ChunkX { get; set; }

    public int ChunkZ { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        ChunkX = stream.ReadInt();
        ChunkZ = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(ChunkX);
        stream.WriteInt(ChunkZ);
    }

    public override int Size() =>
        4
        + 4;
}
