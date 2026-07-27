using System.Text.Json;
using BetaSharp.Loot;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Maps a JSON <c>"Type"</c> key to a behavior instance, mirroring
///     <c>Blocks/Behaviors/BehaviorRegistry.cs</c>.
/// </summary>
internal static class EntityBehaviorRegistry
{
    public delegate object BehaviorFactory(JsonElement json);

    private static readonly Dictionary<string, BehaviorFactory> s_factories = new()
    {
        // Attack
        ["melee"] = json => new MeleeAttackBehavior(Float(json, "range", 2.0F)),
        ["ranged"] = json => new RangedAttackBehavior(Float(json, "range", 10.0F), Int(json, "cooldown_ticks", 30)),
        ["jump"] = json => new JumpAttackBehavior(
            Float(json, "min_range", 2.0F),
            Float(json, "max_range", 6.0F),
            Int(json, "chance_one_in", 10),
            json.TryGetProperty("fallback", out JsonElement fallback) ? (IEntityAttackBehavior)Build(fallback) : null),

        // Targeting
        ["always_hunt"] = json => new AlwaysHuntTargetBehavior(Double(json, "radius", 16.0D)),
        ["darkness_only"] = json => new DarknessOnlyTargetBehavior(Double(json, "radius", 16.0D)),

        // Loot
        ["loot_table"] = json => new LootTableBehavior(LootJson.ParseTable(json)),

        // Lifecycle
        ["slime_split"] = json => new SlimeSplitBehavior(Int(json, "child_count", 4)),
        ["pig_lightning"] = _ => new PigLightningBehavior()
    };

    public static object Build(JsonElement json)
    {
        string type = json.GetProperty("Type").GetString()
            ?? throw new ArgumentException("Behavior entry is missing its 'Type' property.");

        return s_factories.TryGetValue(type, out BehaviorFactory? factory)
            ? factory(json)
            : throw new ArgumentException($"Unknown entity behavior type '{type}'.");
    }

    private static float Float(JsonElement json, string name, float fallback) =>
        json.TryGetProperty(name, out JsonElement value) ? value.GetSingle() : fallback;

    private static double Double(JsonElement json, string name, double fallback) =>
        json.TryGetProperty(name, out JsonElement value) ? value.GetDouble() : fallback;

    private static int Int(JsonElement json, string name, int fallback) =>
        json.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : fallback;
}
