using System.Text.Json.Serialization;
using OmniBlock.Blocks;
using OmniBlock.Registries;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Typed definitions for the non-living entity behaviors. Each holds its configuration as
///     deserialized properties and resolves names to registry objects in <c>Build</c>, so the
///     behavior's own constructor takes plain values and cannot fail.
/// </summary>
internal static class BehaviorDefinitionHelpers
{
    /// <summary>Resolves an item name, accepting blocks that are dropped as items (planks, chest).</summary>
    public static int ItemId(IItemRuntimeView items, string name) => items.Get(ResourceLocation.Parse(name)).Id;

    public static Achievement Achievement(string key) =>
        Achievements.AllAchievements.Find(a => a.TranslationKey == "achievement." + key)
        ?? throw new ArgumentException($"Unknown achievement '{key}'.", nameof(key));
}

public sealed class PrimedExplosiveDefinition : EntityBehaviorDefinition
{
    public int FuseTicks { get; init; } = 80;
    public float Power { get; init; } = 4.0F;
    public string Particle { get; init; } = "smoke";

    public override object Build(in EntityBehaviorBuildContext context) =>
        new PrimedExplosiveBehavior(context.Layout, FuseTicks, Power, Particle);
}

/// <summary>One block this entity may carry, and the object-spawn id it goes out on.</summary>
public sealed class CarriedBlockWireId
{
    [JsonPropertyName("Block")] public string Block { get; init; } = "";
    [JsonPropertyName("Id")] public int Id { get; init; }
}

public sealed class SettleAsBlockDefinition : EntityBehaviorDefinition
{
    public CarriedBlockWireId[] WireIds { get; init; } = [];

    public override object Build(in EntityBehaviorBuildContext context) =>
        new SettleAsBlockBehavior(
            context.Layout,
            ResolveWireIds(context.Blocks));

    private (int id, int Id)[] ResolveWireIds(IBlockRuntimeView blocks) =>
        [.. WireIds.Select(entry => (id: blocks.Get(ResourceLocation.Parse(entry.Block)).Id, entry.Id))];
}

public sealed class LightningStrikeDefinition : EntityBehaviorDefinition
{
    public string ThunderSound { get; init; } = "ambient.weather.thunder";
    public string ExplodeSound { get; init; } = "random.explode";
    public int MinimumFireDifficulty { get; init; } = 2;
    public int ExtraFires { get; init; } = 4;
    public double StrikeRadius { get; init; } = 3.0D;

    public override object Build(in EntityBehaviorBuildContext context) =>
        new LightningStrikeBehavior(context.Layout, ThunderSound, ExplodeSound, MinimumFireDifficulty, ExtraFires, StrikeRadius);
}

/// <summary>An item whose pickup awards an achievement.</summary>
public sealed class PickupAchievement
{
    [JsonPropertyName("Item")] public string Item { get; init; } = "";
    [JsonPropertyName("Achievement")] public string Achievement { get; init; } = "";
}

public sealed class DroppedItemDefinition : EntityBehaviorDefinition
{
    public int DespawnAge { get; init; } = 6000;
    public int Health { get; init; } = 5;
    public PickupAchievement[] PickupAchievements { get; init; } = [];

    public override object Build(in EntityBehaviorBuildContext context)
    {
        var items = context.Items;
        return new DroppedItemBehavior(
            context.Layout,
            DespawnAge,
            Health,
            [
                .. PickupAchievements.Select(entry => (
                    BehaviorDefinitionHelpers.ItemId(items, entry.Item),
                    BehaviorDefinitionHelpers.Achievement(entry.Achievement)))
            ]);
    }
}

/// <summary>The egg's hatch roll: a chance of one hatchling, itself upgradable to several.</summary>
public sealed class HatchOnImpact
{
    [JsonPropertyName("Entity")] public string Entity { get; init; } = "";
    [JsonPropertyName("Chance")] public int Chance { get; init; } = 8;
    [JsonPropertyName("BonusChance")] public int BonusChance { get; init; } = 32;
    [JsonPropertyName("BonusCount")] public int BonusCount { get; init; } = 4;
}

public sealed class ThrownProjectileDefinition : EntityBehaviorDefinition
{
    public string ImpactParticle { get; init; } = "snowballpoof";
    public HatchOnImpact? Hatch { get; init; }

    public override object Build(in EntityBehaviorBuildContext context) =>
        new ThrownProjectileBehavior(
            context.Layout,
            ImpactParticle,
            Hatch is { } hatch
                ? (ResolveEntity(context.EntityTypes, hatch.Entity), hatch.Chance, hatch.BonusChance, hatch.BonusCount)
                : null);

    private static string ResolveEntity(IEntityTypeBuildView entities, string name)
    {
        var key = ResourceLocation.Parse(name);
        if (!entities.TryGet(key, out _)) throw new KeyNotFoundException($"Unknown entity type '{key}'.");
        return key.ToString();
    }
}

public sealed class FireballDefinition : EntityBehaviorDefinition
{
    public float ExplosionPower { get; init; } = 1.0F;

    public override object Build(in EntityBehaviorBuildContext context) =>
        new FireballBehavior(context.Layout, ExplosionPower);
}

public sealed class ArrowDefinition : EntityBehaviorDefinition
{
    public int Damage { get; init; } = 4;

    public override object Build(in EntityBehaviorBuildContext context) =>
        new ArrowBehavior(context.Layout, Damage);
}

public sealed class HangingArtDefinition : EntityBehaviorDefinition
{
    public int CheckInterval { get; init; } = 100;
    public string Drops { get; init; } = "";

    public override object Build(in EntityBehaviorBuildContext context) =>
        new HangingArtBehavior(context.Layout, CheckInterval, context.Items.Get(ResourceLocation.Parse(Drops)));
}

public sealed class FishingBobberDefinition : EntityBehaviorDefinition
{
    public string HeldItem { get; init; } = "";
    public string Catches { get; init; } = "";
    public int BiteDelay { get; init; } = 500;
    public int BiteDelayRaining { get; init; } = 300;
    public double MaxAnglerDistance { get; init; } = 32.0D;

    public override object Build(in EntityBehaviorBuildContext context) =>
        new FishingBobberBehavior(
            context.Layout,
            context.Items.Get(ResourceLocation.Parse(HeldItem)),
            context.Items.Get(ResourceLocation.Parse(Catches)),
            BiteDelay,
            BiteDelayRaining,
            MaxAnglerDistance);
}

/// <summary>One kind of item a vehicle comes apart into, and how many of it.</summary>
public sealed class WreckagePiece
{
    [JsonPropertyName("Item")] public string Item { get; init; } = "";
    [JsonPropertyName("Count")] public int Count { get; init; } = 1;
}

public sealed class BoatDefinition : EntityBehaviorDefinition
{
    public int BreakDamage { get; init; } = 40;
    public WreckagePiece[] Wreckage { get; init; } = [];

    public override object Build(in EntityBehaviorBuildContext context)
    {
        var items = context.Items;
        return new BoatBehavior(
            context.Layout,
            BreakDamage,
            [.. Wreckage.Select(piece => (BehaviorDefinitionHelpers.ItemId(items, piece.Item), piece.Count))]);
    }
}

/// <summary>One cart kind: its object-spawn id and the pieces it breaks into.</summary>
public sealed class MinecartKind
{
    [JsonPropertyName("Type")] public int Type { get; init; }
    [JsonPropertyName("Id")] public int Id { get; init; }
    [JsonPropertyName("Drops")] public string[] Drops { get; init; } = [];
}

public sealed class MinecartDefinition : EntityBehaviorDefinition
{
    public int BreakDamage { get; init; } = 40;
    public string FuelItem { get; init; } = "";
    public int FuelPerCoal { get; init; } = 1200;
    public MinecartKind[] WireIds { get; init; } = [];

    public override object Build(in EntityBehaviorBuildContext context)
    {
        var items = context.Items;
        return new MinecartBehavior(
            context.Layout,
            BreakDamage,
            BehaviorDefinitionHelpers.ItemId(context.Items, FuelItem),
            FuelPerCoal,
            WireIds.ToDictionary(kind => kind.Type, kind => kind.Id),
            WireIds.ToDictionary(kind => kind.Type, kind => kind.Drops.Select(drop => BehaviorDefinitionHelpers.ItemId(items, drop)).ToArray()));
    }
}
