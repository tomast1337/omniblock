using System.Text.Json;
using System.Text.Json.Serialization;
using BetaSharp.Entities.State;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     One behavior entry in an entity's JSON, deserialized into typed properties rather than read
///     field-by-field out of a <see cref="JsonElement" />. Mirrors
///     <c>Items/Behaviors/ItemBehaviorDefinition.cs</c>: the definition holds the configuration,
///     <see cref="Build" /> turns it into the behavior, and the behavior's own constructor takes
///     plain values and cannot fail.
///     <para>
///         Behavior parameters are snake_case on disk (<c>fuse_ticks</c>), resolved by the naming
///         policy in <see cref="EntityBehaviorDefinitionConverter" />, so property names stay
///         idiomatic C# without an attribute on every one.
///     </para>
/// </summary>
[JsonConverter(typeof(EntityBehaviorDefinitionConverter))]
public abstract class EntityBehaviorDefinition
{
    /// <summary>Which capability slots this one instance fills.</summary>
    [JsonPropertyName("Slots")]
    public string[] Slots { get; init; } = [];

    /// <summary>
    ///     Constructs the behavior. Runs once per <see cref="EntityType" /> at load, never per spawn.
    ///     Resolving names to registry objects belongs here, not in the behavior's constructor.
    /// </summary>
    public abstract object Build(in EntityBehaviorBuildContext context);
}

/// <summary>
///     What a behavior needs at construction beyond its own configuration: somewhere to declare
///     per-entity state slots, and the owning definition for resolving synced property names.
///     Deliberately carries no JSON — parsing is finished by the time this is handed out.
/// </summary>
/// <param name="Definition">The owning entity's definition, for resolving synced property names.</param>
/// <param name="Layout">The type's state layout, for declaring unsynced per-entity fields.</param>
public readonly record struct EntityBehaviorBuildContext(EntityDefinition Definition, EntityStateLayout Layout)
{
    public StateHandle<int> DeclareInt(int initial = 0) => Layout.DeclareInt(initial);
    public StateHandle<long> DeclareLong() => Layout.DeclareLong();
    public StateHandle<float> DeclareFloat(float initial = 0.0F) => Layout.DeclareFloat(initial);
    public StateHandle<double> DeclareDouble(double initial = 0.0D) => Layout.DeclareDouble(initial);
    public StateHandle<bool> DeclareBool(bool initial = false) => Layout.DeclareBool(initial);
    public StateHandle<T> DeclareRef<T>() where T : class => Layout.DeclareRef<T>();

    /// <summary>Resolves a synced property declared in the entity's JSON to a typed, id-carrying handle.</summary>
    public SyncedHandle<T> Synced<T>(string name) => SyncedPropertyFactory.Resolve<T>(Definition, name);
}
