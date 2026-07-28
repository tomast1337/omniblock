using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityType(Func<IWorldContext, EntityType, Entity> factory, Type baseType, string id, EntityDefinition? definition = null)
{
    public Type BaseType { get; } = baseType;
    public string Id { get; } = id;

    /// <summary>
    ///     Configuration for this type, or <c>null</c> for entities that carry none — projectiles,
    ///     vehicles, paintings, and the player. Phase 4 swaps the registered value for one loaded from
    ///     JSON; mobs read it through here rather than from a static field so that swap reaches them.
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
    ///     being recovered from its C# class. This is what lets several registered types share one
    ///     class — the step that makes a mob subclass optional rather than mandatory.
    /// </summary>
    public Entity Create(IWorldContext world)
    {
        Entity entity = factory(world, this);

        // After the constructor rather than inside it, so a behavior rolling per-individual state —
        // a slime's size, which resizes the body it is given — sees a finished entity.
        if (entity is EntityLiving living) Behaviors.Lifecycle?.OnCreated(living);

        return entity;
    }

    /// <summary>Definition accessor for mob constructors, which cannot proceed without one.</summary>
    public EntityDefinition RequireDefinition() =>
        Definition ?? throw new InvalidOperationException($"Entity type '{Id}' was registered without an {nameof(EntityDefinition)}.");
}
