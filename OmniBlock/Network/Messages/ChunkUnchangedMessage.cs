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
[WireMessage("omniblock:chunk_unchanged")]
public sealed partial class ChunkUnchangedMessage : Message
{
    /// <summary>
    ///     Bulk, matching <see cref="ChunkDataMessage" />. It is small, but it is the same traffic
    ///     class and reordering it ahead of a chunk it replaces would gain nothing.
    /// </summary>
    public override SendPriority Priority => SendPriority.Normal;

    [WireField]
    public int ChunkX { get; set; }

    [WireField]
    public int ChunkZ { get; set; }
}
