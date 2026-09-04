using OmniBlock.Entities.State;

namespace OmniBlock.Entities;

/// <summary>
///     Every capability slot for one <see cref="EntityType" />, built once at load and shared by all
///     instances of that type, the same lifetime blocks give their behaviors.
///     <see cref="StateLayout" /> is the companion: it sizes the per-entity
///     <see cref="EntityState" /> these shared behaviors read through.
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
    public IEntityInteractable? Interactable { get; internal set; }
    public IEntityPhysics? Physics { get; internal set; }

    /// <summary>
    ///     Finds a behavior of a given kind in any slot, descending into composites and decorators.
    ///     Renderers use it to read behavior state (a ghast's charge counter, a chicken's wing angle)
    ///     without knowing which slot the definition put it in.
    /// </summary>
    public T? Find<T>() where T : class
    {
        foreach (var slot in new object?[] { Ticker, Attack, Targeting, Loot, Lifecycle, Persistence, Interactable, Physics })
        {
            if (FindIn(slot) is { } found)
            {
                return found;
            }
        }

        return null;

        static T? FindIn(object? behavior) => behavior switch
        {
            T match => match,
            IEntityBehaviorGroup group => group.Children.Select(FindIn).FirstOrDefault(found => found is not null),
            _ => null
        };
    }
}
