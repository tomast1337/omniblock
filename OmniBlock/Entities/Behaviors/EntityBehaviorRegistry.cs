using System.Text.Json;
using OmniBlock.Items;
using OmniBlock.Loot;

namespace OmniBlock.Entities.Behaviors;

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
        ["melee"] = (in c) => new MeleeAttackBehavior(c.Float("range", 2.0F)),
        ["bite"] = (in c) => new BiteAttackBehavior(
            c.Float("range", 1.5F),
            c.Int("damage", 2),
            c.Int("tamed_damage", 4)),
        ["ranged"] = (in c) => new RangedAttackBehavior(c.Float("range", 10.0F), c.Int("cooldown_ticks", 30)),
        ["jump"] = (in c) => new JumpAttackBehavior(
            c.Float("min_range", 2.0F),
            c.Float("max_range", 6.0F),
            c.Int("chance_one_in", 10),
            c.Json.TryGetProperty("fallback", out JsonElement fallback)
                ? (IEntityAttackBehavior)Build(c with
                {
                    Json = fallback
                })
                : null),

        ["lose_target_in_daylight"] = (in c) => new LoseTargetInDaylightBehavior(
            (IEntityAttackBehavior)Build(c with
            {
                Json = c.Json.GetProperty("inner")
            }),
            c.Float("brightness_threshold", 0.5F),
            c.Int("chance_one_in", 100)),

        // Targeting
        ["always_hunt"] = (in c) => new AlwaysHuntTargetBehavior(c.Double("radius", 16.0D)),
        ["darkness_only"] = (in c) => new DarknessOnlyTargetBehavior(c.Double("radius", 16.0D)),

        // Loot
        ["loot_table"] = (in c) => new LootTableBehavior(LootJson.ParseTable(c.Json, c.Items)),

        // Interactable
        ["swap_held_item"] = (in c) => new SwapHeldItemBehavior(
            c.Items.Get(ResourceLocation.Parse(c.Json.GetProperty("required").GetString()!)),
            c.Items.Get(ResourceLocation.Parse(c.Json.GetProperty("result").GetString()!))),
        ["ride_if_saddled"] = (in c) => new RideIfSaddledBehavior(c),
        ["wool"] = (in c) => new WoolBehavior(c),
        ["contact_damage"] = (in c) => new ContactDamageBehavior(
            c.Double("reach_per_size", 0.6D),
            c.Int("minimum_size", 2),
            c.Json.TryGetProperty("sound", out JsonElement s) ? s.GetString() ?? "" : ""),

        // Attack + Ticker + Lifecycle, all moving one countdown
        ["fuse"] = (in c) => new FuseBehavior(c),

        // Physics
        ["ignore_fall_damage"] = (in c) => new IgnoreFallDamageBehavior(),
        ["grazing_animal"] = (in c) => new GrazingAnimalBehavior(),
        ["flap_descent"] = (in c) => new FlapDescentBehavior(c),
        ["light_seeking_path"] = (in c) => new LightSeekingPathBehavior(),
        ["wall_climb"] = (in c) => new WallClimbBehavior(),
        ["spawn_ignoring_light"] = (in c) => new SpawnIgnoringLightBehavior(
            !c.Json.TryGetProperty("requires_difficulty", out JsonElement d) || d.GetBoolean(),
            c.Int("chance_one_in", 1)),
        ["flying_movement"] = (in c) => new FlyingMovementBehavior(),
        ["spawn_in_fluid"] = (in c) => new SpawnInFluidBehavior(),
        ["slime_chunk_spawn"] = (in c) => new SlimeChunkSpawnBehavior(
            c.Json.GetProperty("chunk_seed").GetInt64(),
            c.Int("chance_one_in", 10),
            c.Int("chunk_chance_one_in", 10),
            c.Double("max_height", 16.0D),
            c.Int("difficulty_free_size", 1)),

        // Lifecycle + Persistence: size is the body, so one behavior owns rolling it, applying it and
        // saving it
        ["sized_body"] = (in c) => new SizedBodyBehavior(c),

        // Ticker + Lifecycle, all moving one hop
        ["hopping"] = (in c) => new HoppingBehavior(c),

        // Interactable + Persistence + Targeting + Ticker + Lifecycle + Physics, all reading one
        // packed flags byte
        ["tameable"] = (in c) => new TameableBehavior(c),
        ["follow_owner"] = (in c) => new FollowOwnerBehavior(c),
        ["head_tilt"] = (in c) => new HeadTiltBehavior(c),
        ["shake_off_water"] = (in c) => new ShakeOffWaterBehavior(c),

        // Physics + Ticker, all moving one swim cycle
        ["jet_swim"] = (in c) => new JetSwimBehavior(c),

        // Physics + Ticker + Lifecycle: everything shared by a mob that hunts the player
        ["hostile_monster"] = (in c) => new HostileMonsterBehavior(),

        // Ticker
        ["despawn_on_peaceful"] = (in c) => new DespawnOnPeacefulBehavior(),
        ["flying_wander"] = (in c) => new FlyingWanderBehavior(c),
        ["fireball_attack"] = (in c) => new FireballAttackBehavior(c),

        // Any slot: several behaviors sharing one
        ["all"] = (in c) => new CompositeBehavior(c),

        // Ticker + Targeting + Lifecycle + Persistence, all reading one anger timer
        ["anger"] = (in c) => new AngerBehavior(c),
        ["rider_fall_stat"] = (in c) => new RiderFallStatBehavior(
            Achievement(c.Json.GetProperty("achievement").GetString()!),
            c.Float("minimum_distance", 5.0F)),

        // Ticker
        ["burn_in_daylight"] = (in c) => new BurnInDaylightBehavior(c.Int("fire_ticks", 300)),
        ["lay_eggs"] = (in c) => new LayEggsBehavior(c),

        // Lifecycle
        ["split_on_death"] = (in c) => new SplitOnDeathBehavior(c.Int("child_count", 4)),
        ["spawn_rider"] = (in c) => new SpawnRiderBehavior(
            c.Json.GetProperty("rider").GetString()!,
            c.Int("chance_one_in", 100)),
        ["lightning_conversion"] = (in c) => new LightningConversionBehavior(c.Json.GetProperty("becomes").GetString()!)
    };

    /// <summary>
    ///     Resolves an achievement by its short key (<c>"flyPig"</c>). Achievements have no registry
    ///     of their own, so this matches on the translation key they are all built from.
    /// </summary>
    internal static Achievement Achievement(string key) =>
        Achievements.AllAchievements.Find(a => a.TranslationKey == "achievement." + key)
        ?? throw new ArgumentException($"Unknown achievement '{key}'.", nameof(key));

    public static object Build(in EntityBehaviorContext context)
    {
        string type = context.Json.GetProperty("Type").GetString()
                      ?? throw new ArgumentException("Behavior entry is missing its 'Type' property.");

        return s_factories.TryGetValue(type, out BehaviorFactory? factory)
            ? factory(context)
            : throw new ArgumentException($"Unknown entity behavior type '{type}'.");
    }
}
