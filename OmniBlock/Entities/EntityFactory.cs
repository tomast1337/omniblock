using OmniBlock.Entities.Behaviors;
using OmniBlock.Entities.State;
using System.Text.Json;

namespace OmniBlock.Entities;

/// <summary>
///     Builds an <see cref="EntityType" />'s capability slots from its JSON, matching
///     <c>BlockFactory.AttachBehaviors</c>: one instance per array entry, assigned to every slot that
///     entry names.
///     <para>
///         Runs once per type at load. Behaviors are then shared by every instance, so per-entity
///         mutable state lives in <see cref="EntityState" />, never on the behavior.
///     </para>
/// </summary>
internal static class EntityFactory
{
    public static EntityBehaviorSet BuildBehaviors(
        EntityDefinition definition,
        Type entityType,
        in EntityBuildContext dependencies,
        IEntityBehaviorProviderRegistry providers)
    {
        EntityStateLayout layout = new();
        EntityBehaviorSet set = new(layout);
        HashSet<string> occupiedSlots = [];

        EntityBehaviorBuildContext context = new(
            definition, layout, dependencies.Blocks, dependencies.Items, dependencies.EntityTypes, providers);

        foreach (JsonElement entry in definition.Behaviors)
        {
            // Runs inside a static constructor chain, so without this the failure surfaces as a bare
            // TypeInitializationException naming neither the entity nor the behavior.
            object behavior;
            try
            {
                behavior = context.Build(entry);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to build {entry} on entity '{definition.Name}': {ex.Message}", ex);
            }

            if (!entry.TryGetProperty("Slots", out JsonElement slots) || slots.GetArrayLength() == 0)
            {
                throw new ArgumentException($"Behavior {entry} on '{definition.Name}' names no slots.");
            }

            foreach (JsonElement slotElement in slots.EnumerateArray())
            {
                string slot = slotElement.GetString()
                              ?? throw new ArgumentException($"Behavior on '{definition.Name}' has a null slot.");
                if (!occupiedSlots.Add(slot))
                    throw new ArgumentException($"Entity '{definition.Name}' declares duplicate behavior slot '{slot}'.");
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
            case "Interactable":
                set.Interactable = Cast<IEntityInteractable>(behavior, definition, slot);
                break;
            case "Physics":
                set.Physics = Cast<IEntityPhysics>(behavior, definition, slot);
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
    ///     them for anything else fails at load instead of leaving a null slot to find at runtime.
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
