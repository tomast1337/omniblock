using System.Text.Json;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Entities.State;

namespace BetaSharp.Entities;

/// <summary>
///     Builds an <see cref="EntityType" />'s capability slots from its JSON, mirroring
///     <c>BlockFactory.AttachBehaviors</c>: one instance per array entry, assigned to every slot that
///     entry names, rather than probing a shared instance against every interface.
///     <para>
///         Runs once per type at load. Behaviors are then shared by every instance, so per-entity
///         mutable state lives in <see cref="EntityState" /> rather than on the behavior.
///     </para>
/// </summary>
internal static class EntityFactory
{
    public static EntityBehaviorSet BuildBehaviors(EntityDefinition definition, Type entityType)
    {
        EntityStateLayout layout = new();
        EntityBehaviorSet set = new(layout);

        foreach (JsonElement entry in definition.Behaviors)
        {
            EntityBehaviorContext context = new(entry, definition, layout);
            object behavior = EntityBehaviorRegistry.Build(context);

            if (!entry.TryGetProperty("Slots", out JsonElement slots))
            {
                throw new ArgumentException($"Behavior entry on '{definition.Name}' is missing its 'Slots' array.");
            }

            foreach (JsonElement slotJson in slots.EnumerateArray())
            {
                string slot = slotJson.GetString()
                    ?? throw new ArgumentException($"Behavior entry on '{definition.Name}' has a null entry in 'Slots'.");

                Attach(set, definition, entityType, slot, behavior);
            }
        }

        return set;
    }

    private static void Attach(EntityBehaviorSet set, EntityDefinition definition, Type entityType, string slot, object behavior)
    {
        switch (slot)
        {
            case "Ticker":
                set.Ticker = Cast<IEntityTicker>(behavior, definition, slot);
                break;
            case "Attack":
                RequireCreature(definition, entityType, slot);
                set.Attack = Cast<IEntityAttackBehavior>(behavior, definition, slot);
                break;
            case "Targeting":
                RequireCreature(definition, entityType, slot);
                set.Targeting = Cast<IEntityTargetBehavior>(behavior, definition, slot);
                break;
            case "Loot":
                set.Loot = Cast<IEntityLootBehavior>(behavior, definition, slot);
                break;
            case "Persistence":
                set.Persistence = Cast<IEntityPersistence>(behavior, definition, slot);
                break;
            case "Lifecycle":
                set.Lifecycle = Cast<IEntityLifecycle>(behavior, definition, slot);
                break;
            default:
                throw new ArgumentException($"Unknown behavior slot '{slot}' on '{definition.Name}'.");
        }
    }

    /// <summary>
    ///     Attack and targeting are declared on <see cref="EntityCreature" />, so a definition naming
    ///     them for anything else is a load-time error rather than a null slot discovered at runtime.
    /// </summary>
    private static void RequireCreature(EntityDefinition definition, Type entityType, string slot)
    {
        if (!typeof(EntityCreature).IsAssignableFrom(entityType))
        {
            throw new ArgumentException(
                $"'{definition.Name}' declares a '{slot}' behavior, but {entityType.Name} is not an {nameof(EntityCreature)}.");
        }
    }

    private static T Cast<T>(object behavior, EntityDefinition definition, string slot) =>
        behavior is T typed
            ? typed
            : throw new ArgumentException($"Behavior on '{definition.Name}' does not implement {typeof(T).Name} required by slot '{slot}'.");
}
