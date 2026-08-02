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

        GeneratedMessages.RegisterAll(registry);

        // The two messages whose payloads the generator has no encoding for: a list of chunk
        // position/hash pairs, and a variable-length run of field-masked entity deltas. Both are
        // shapes a declarative field list cannot express, and inventing a list encoding to cover one
        // of them would be a worse format than the one they already have. They stay hand-written and
        // are registered here; everything above this line is derived from the declarations.
        registry.Register(ChunkCacheOfferMessage.Id, 1, static () => new ChunkCacheOfferMessage());
        registry.Register(EntitySnapshotMessage.Id, 1, static () => new EntitySnapshotMessage());
        registry.Register(RegistryDataMessage.Id, 1, static () => new RegistryDataMessage());
        registry.Register(ChunkDeltaUpdateMessage.Id, 1, static () => new ChunkDeltaUpdateMessage());
        registry.Register(ExplosionMessage.Id, 1, static () => new ExplosionMessage());
    }
}
