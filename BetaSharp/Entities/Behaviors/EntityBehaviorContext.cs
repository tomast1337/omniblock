using System.Text.Json;
using BetaSharp.Entities.State;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     What a behavior gets while being constructed, once per <see cref="EntityType" /> at load.
///     A behavior declares the per-entity state it needs and resolves its synced properties here,
///     keeping handles as its own fields; at runtime it reads them off whichever entity it is
///     given.
/// </summary>
/// <param name="Json">This behavior's own JSON entry, for its parameters.</param>
/// <param name="Definition">The owning entity's definition, for resolving synced property names.</param>
/// <param name="Layout">The type's state layout, for declaring unsynced fields.</param>
public readonly record struct EntityBehaviorContext(JsonElement Json, EntityDefinition Definition, EntityStateLayout Layout)
{
    public StateHandle<int> DeclareInt(int initial = 0) => Layout.DeclareInt(initial);
    public StateHandle<float> DeclareFloat(float initial = 0.0F) => Layout.DeclareFloat(initial);
    public StateHandle<double> DeclareDouble(double initial = 0.0D) => Layout.DeclareDouble(initial);
    public StateHandle<bool> DeclareBool(bool initial = false) => Layout.DeclareBool(initial);
    public StateHandle<T> DeclareRef<T>() where T : class => Layout.DeclareRef<T>();

    /// <summary>Resolves a synced property declared in the entity's JSON to a typed, id-carrying handle.</summary>
    public SyncedHandle<T> Synced<T>(string name) => SyncedPropertyFactory.Resolve<T>(Definition, name);

    public float Float(string name, float fallback) => Json.TryGetProperty(name, out JsonElement v) ? v.GetSingle() : fallback;
    public double Double(string name, double fallback) => Json.TryGetProperty(name, out JsonElement v) ? v.GetDouble() : fallback;
    public int Int(string name, int fallback) => Json.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : fallback;
    public bool Bool(string name, bool fallback) => Json.TryGetProperty(name, out JsonElement v) ? v.GetBoolean() : fallback;
}
