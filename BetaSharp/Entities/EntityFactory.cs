using System.Text.Json;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Entities;

/// <summary>
///     Wires a mob's JSON-declared behaviors into its capability slots, mirroring
///     <c>BlockFactory.AttachBehaviors</c>: one instance per array entry, assigned to every slot that
///     entry names, rather than one shared instance probed against every interface.
/// </summary>
internal static class EntityFactory
{
    public static void AttachBehaviors(EntityLiving entity, EntityDefinition definition)
    {
        foreach (JsonElement entry in definition.Behaviors)
        {
            object behavior = EntityBehaviorRegistry.Build(entry);

            if (!entry.TryGetProperty("Slots", out JsonElement slots))
            {
                throw new ArgumentException($"Behavior entry on '{definition.Name}' is missing its 'Slots' array.");
            }

            foreach (JsonElement slotJson in slots.EnumerateArray())
            {
                string slot = slotJson.GetString()
                    ?? throw new ArgumentException($"Behavior entry on '{definition.Name}' has a null entry in 'Slots'.");

                Attach(entity, definition, slot, behavior);
            }
        }
    }

    private static void Attach(EntityLiving entity, EntityDefinition definition, string slot, object behavior)
    {
        switch (slot)
        {
            case "Attack":
                RequireCreature(entity, definition, slot).Attack = Cast<IEntityAttackBehavior>(behavior, definition, slot);
                break;
            case "Targeting":
                RequireCreature(entity, definition, slot).Targeting = Cast<IEntityTargetBehavior>(behavior, definition, slot);
                break;
            case "Loot":
                entity.Loot = Cast<IEntityLootBehavior>(behavior, definition, slot);
                break;
            case "Lifecycle":
                entity.Lifecycle = Cast<IEntityLifecycle>(behavior, definition, slot);
                break;
            default:
                throw new ArgumentException($"Unknown behavior slot '{slot}' on '{definition.Name}'.");
        }
    }

    private static EntityCreature RequireCreature(EntityLiving entity, EntityDefinition definition, string slot) =>
        entity as EntityCreature
        ?? throw new ArgumentException($"'{definition.Name}' declares a '{slot}' behavior, but {entity.GetType().Name} is not an {nameof(EntityCreature)}.");

    private static T Cast<T>(object behavior, EntityDefinition definition, string slot) =>
        behavior is T typed
            ? typed
            : throw new ArgumentException($"Behavior on '{definition.Name}' does not implement {typeof(T).Name} required by slot '{slot}'.");
}
