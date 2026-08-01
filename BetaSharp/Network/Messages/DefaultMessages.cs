namespace BetaSharp.Network.Messages;

/// <summary>
///     The message types the base game registers, in one place so both peers register the same set.
///     <para>
///         Shared rather than duplicated because the two sides fail differently and neither failure
///         is loud. The server's registered set becomes the ID table it advertises; a client missing
///         a key sees a hole and silently drops that message, and a client registering a key the
///         server never advertises simply cannot send it. Both look like a feature quietly not
///         working, so the way to not have that bug is to make it impossible to register different
///         sets rather than to remember to keep two lists aligned.
///     </para>
///     <para>
///         Mods register alongside this, not inside it. Ordering does not matter: IDs come from the
///         sorted key set, not from registration sequence.
///     </para>
/// </summary>
public static class DefaultMessages
{
    public static void RegisterAll(MessageRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(TimeSyncRequestMessage.Id, 1, () => new TimeSyncRequestMessage());
        registry.Register(TimeSyncResponseMessage.Id, 1, () => new TimeSyncResponseMessage());
        registry.Register(TickStampMessage.Id, 1, () => new TickStampMessage());
        registry.Register(ChunkDataMessage.Id, 1, () => new ChunkDataMessage());
        registry.Register(ChunkCacheOfferMessage.Id, 1, () => new ChunkCacheOfferMessage());
        registry.Register(ChunkUnchangedMessage.Id, 1, () => new ChunkUnchangedMessage());
        registry.Register(InteractEntityMessage.Id, 1, () => new InteractEntityMessage());
    }
}
