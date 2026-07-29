using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityType(Func<IWorldContext, EntityType, Entity> factory, Type baseType, string id, EntityDefinition? definition = null)
{
    public Type BaseType { get; } = baseType;
    public string Id { get; } = id;

    /// <summary>
    ///     Configuration for this type, loaded from <c>assets/entity/*.json</c>, or <c>null</c> for
    ///     entities that carry none (currently only the player).
    /// </summary>
    public EntityDefinition? Definition { get; } = definition;

    /// <summary>
    ///     Capability slots and state layout for this type, built once from <see cref="Definition" />
    ///     at registration and shared by every instance.
    /// </summary>
    public EntityBehaviorSet Behaviors { get; } =
        definition is null ? EntityBehaviorSet.Empty : EntityFactory.BuildBehaviors(definition, baseType);

    /// <summary>
    ///     Hands the type to the entity it creates, so identity travels with the instance instead of
    ///     being recovered from its C# class. Several registered types can therefore share one class.
    /// </summary>
    public Entity Create(IWorldContext world)
    {
        Entity entity = factory(world, this);

        // After the constructor, not inside it: a behavior that rolls per-individual state (a slime's
        // size, which resizes the body) needs a finished entity.
        Behaviors.Lifecycle?.OnCreated(entity);

        return entity;
    }

    /// <summary>Definition accessor for mob constructors, which cannot proceed without one.</summary>
    public EntityDefinition RequireDefinition() =>
        Definition ?? throw new InvalidOperationException($"Entity type '{Id}' was registered without an {nameof(EntityDefinition)}.");
}
