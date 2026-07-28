using System.Text.Json;
using BetaSharp.Loot;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Maps a JSON <c>"Type"</c> key to a behavior instance, mirroring
///     <c>Blocks/Behaviors/BehaviorRegistry.cs</c>. Factories run once per
///     <see cref="EntityType" /> at load, never per spawn.
/// </summary>
internal static class EntityBehaviorRegistry
{
    public delegate object BehaviorFactory(in EntityBehaviorContext context);

    private static readonly Dictionary<string, BehaviorFactory> s_factories = new()
    {
        // Attack
        ["melee"] = (in EntityBehaviorContext c) => new MeleeAttackBehavior(c.Float("range", 2.0F)),
        ["ranged"] = (in EntityBehaviorContext c) => new RangedAttackBehavior(c.Float("range", 10.0F), c.Int("cooldown_ticks", 30)),
        ["jump"] = (in EntityBehaviorContext c) => new JumpAttackBehavior(
            c.Float("min_range", 2.0F),
            c.Float("max_range", 6.0F),
            c.Int("chance_one_in", 10),
            c.Json.TryGetProperty("fallback", out JsonElement fallback)
                ? (IEntityAttackBehavior)Build(c with { Json = fallback })
                : null),

        // Targeting
        ["always_hunt"] = (in EntityBehaviorContext c) => new AlwaysHuntTargetBehavior(c.Double("radius", 16.0D)),
        ["darkness_only"] = (in EntityBehaviorContext c) => new DarknessOnlyTargetBehavior(c.Double("radius", 16.0D)),

        // Loot
        ["loot_table"] = (in EntityBehaviorContext c) => new LootTableBehavior(LootJson.ParseTable(c.Json)),

        // Interactable
        ["swap_held_item"] = (in EntityBehaviorContext c) => new SwapHeldItemBehavior(
            Items.Item.ByName(ResourceLocation.Parse(c.Json.GetProperty("required").GetString()!).Path),
            Items.Item.ByName(ResourceLocation.Parse(c.Json.GetProperty("result").GetString()!).Path)),
        ["ride_if_saddled"] = (in EntityBehaviorContext c) => new RideIfSaddledBehavior(c),
        ["contact_damage"] = (in EntityBehaviorContext c) => new ContactDamageBehavior(
            c.Double("reach_per_size", 0.6D),
            c.Int("minimum_size", 2),
            c.Json.TryGetProperty("sound", out JsonElement s) ? s.GetString() ?? "" : ""),

        // Ticker
        ["burn_in_daylight"] = (in EntityBehaviorContext c) => new BurnInDaylightBehavior(c.Int("fire_ticks", 300)),
        ["lay_eggs"] = (in EntityBehaviorContext c) => new LayEggsBehavior(c),

        // Lifecycle
        ["slime_split"] = (in EntityBehaviorContext c) => new SlimeSplitBehavior(c.Int("child_count", 4)),
        ["pig_lightning"] = (in EntityBehaviorContext c) => new PigLightningBehavior()
    };

    public static object Build(in EntityBehaviorContext context)
    {
        string type = context.Json.GetProperty("Type").GetString()
            ?? throw new ArgumentException("Behavior entry is missing its 'Type' property.");

        return s_factories.TryGetValue(type, out BehaviorFactory? factory)
            ? factory(context)
            : throw new ArgumentException($"Unknown entity behavior type '{type}'.");
    }
}
