namespace OmniBlock.Entities.State;

/// <summary>
///     A behavior's typed reference to a declared synced property. Holds the wire id resolved from
///     the entity definition at load, so behavior code never hardcodes a datawatcher index.
/// </summary>
public readonly record struct SyncedHandle<T>(int Id);
