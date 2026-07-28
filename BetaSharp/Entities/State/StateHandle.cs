namespace BetaSharp.Entities.State;

/// <summary>
///     A typed slot in an entity's <see cref="EntityState" />, handed out by
///     <see cref="EntityStateLayout" /> when a behavior declares a field.
///     <para>
///         Behaviors are shared per <see cref="EntityType" />, so they cannot hold per-entity values
///         directly. They hold handles instead — resolved once at load — and read through them:
///         <c>entity.State[_timeSinceIgnited]</c>. The indexer overloads keep this type-safe and
///         allocation-free, unlike a string-keyed bag.
///     </para>
/// </summary>
public readonly record struct StateHandle<T>(int Index);
