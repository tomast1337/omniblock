using BetaSharp.Entities.State;

namespace BetaSharp.Entities;

/// <summary>
///     Every capability slot for one <see cref="EntityType" />, built once at load and shared by all
///     instances of that type — the same lifetime blocks give their behaviors.
///     <para>
///         This replaces rebuilding behaviors (and re-parsing their JSON) on every spawn.
///         <see cref="StateLayout" /> is the companion: it sizes the per-entity
///         <see cref="EntityState" /> these shared behaviors read through.
///     </para>
/// </summary>
public sealed class EntityBehaviorSet
{
    public static readonly EntityBehaviorSet Empty = new(new EntityStateLayout());

    public EntityBehaviorSet(EntityStateLayout layout) => StateLayout = layout;

    public EntityStateLayout StateLayout { get; }

    public IEntityTicker? Ticker { get; internal set; }
    public IEntityAttackBehavior? Attack { get; internal set; }
    public IEntityTargetBehavior? Targeting { get; internal set; }
    public IEntityLootBehavior? Loot { get; internal set; }
    public IEntityLifecycle? Lifecycle { get; internal set; }
    public IEntityPersistence? Persistence { get; internal set; }
}
