using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityType(Func<IWorldContext, Entity> factory, Type baseType, string id, EntityDefinition? definition = null)
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

    public Entity Create(IWorldContext world) => factory(world);

    /// <summary>Definition accessor for mob constructors, which cannot proceed without one.</summary>
    public EntityDefinition RequireDefinition() =>
        Definition ?? throw new InvalidOperationException($"Entity type '{Id}' was registered without an {nameof(EntityDefinition)}.");
}
