using System.Text.Json;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Maps a JSON <c>"Type"</c> key to the <see cref="EntityBehaviorDefinition" /> subclass that
///     describes it. The table names a type and deserialization fills its properties, so no entry
///     reaches into a <see cref="JsonElement" /> by hand.
/// </summary>
internal static class EntityBehaviorDefinitionRegistry
{
    private static readonly Dictionary<string, Type> s_types = new(StringComparer.Ordinal)
    {
        // Non-living entities.
        ["primed_explosive"] = typeof(PrimedExplosiveDefinition),
        ["settle_as_block"] = typeof(SettleAsBlockDefinition),
        ["lightning_strike"] = typeof(LightningStrikeDefinition),
        ["dropped_item"] = typeof(DroppedItemDefinition),
        ["thrown_projectile"] = typeof(ThrownProjectileDefinition),
        ["fireball"] = typeof(FireballDefinition),
        ["arrow"] = typeof(ArrowDefinition),
        ["hanging_art"] = typeof(HangingArtDefinition),
        ["fishing_bobber"] = typeof(FishingBobberDefinition),
        ["boat"] = typeof(BoatDefinition),
        ["minecart"] = typeof(MinecartDefinition)
    };

    /// <summary>The subclass describing this behavior type, or <c>null</c> if it has none yet.</summary>
    public static Type? Resolve(string typeName) => s_types.GetValueOrDefault(typeName);
}
