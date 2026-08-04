using BetaSharp.Worlds.Maps;

namespace BetaSharp.Blocks.Materials;

/// <summary>
///     Immutable physical properties shared by blocks, loaded from <c>assets/material/*.json</c>.
///     <para>
///         Instances are canonical: every key maps to exactly one instance for the lifetime of the
///         process (built once by <see cref="MaterialRegistry" />), so reference equality
///         (<c>material == Material.Water</c>) is the correct comparison. Do not construct materials
///         outside the registry.
///     </para>
/// </summary>
public sealed class Material
{
    public static Material Air => MaterialRegistry.Get("air");
    public static Material Wood => MaterialRegistry.Get("wood");
    public static Material Stone => MaterialRegistry.Get("stone");
    public static Material Metal => MaterialRegistry.Get("metal");
    public static Material Water => MaterialRegistry.Get("water");
    public static Material Lava => MaterialRegistry.Get("lava");
    public static Material Plant => MaterialRegistry.Get("plant");
    public static Material Fire => MaterialRegistry.Get("fire");
    public static Material Sand => MaterialRegistry.Get("sand");
    public static Material Glass => MaterialRegistry.Get("glass");
    public static Material Ice => MaterialRegistry.Get("ice");
    public static Material SnowLayer => MaterialRegistry.Get("snow_layer");
    public static Material SnowBlock => MaterialRegistry.Get("snow_block");
    public static Material NetherPortal => MaterialRegistry.Get("nether_portal");

    public required MapColor MapColor { get; init; }
    public bool IsFluid { get; init; }
    public bool IsSolid { get; init; } = true;
    public bool BlocksVision { get; init; } = true;
    public bool BlocksMovement { get; init; } = true;
    public bool IsBurnable { get; init; }
    public bool IsReplaceable { get; init; }
    public bool IsHandHarvestable { get; init; } = true;
    public bool IsTransparent { get; init; }
    public PistonBehavior PistonBehavior { get; init; }

    public bool Suffocates => !IsTransparent && BlocksMovement;
}
