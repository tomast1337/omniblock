using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;
using BetaSharp.Worlds.Maps;

namespace BetaSharp.Blocks.Materials;

/// <summary>
///     Process-global registry of canonical <see cref="Material" /> instances, loaded once from
///     <c>assets/material/*.json</c> during <see cref="Bootstrap.Initialize" /> — before anything
///     touches <see cref="Block" />, whose static fields consume materials.
///     <para>
///         <see cref="Get" /> always returns the same instance for a key, which is what keeps the many
///         <c>material == Material.Water</c> reference comparisons across the codebase correct.
///     </para>
/// </summary>
public static class MaterialRegistry
{
    private static readonly CanonicalRegistry<Material> s_registry = new("material");

    // MapColor creation order is serialized into map data, so MapColor instances stay
    // code-side; JSON references them by name through this lookup.
    private static readonly FrozenDictionary<string, MapColor> s_mapColors = new Dictionary<string, MapColor>
    {
        ["air"] = MapColor.Air,
        ["grass"] = MapColor.Grass,
        ["sand"] = MapColor.Sand,
        ["cloth"] = MapColor.Cloth,
        ["tnt"] = MapColor.TNT,
        ["ice"] = MapColor.Ice,
        ["iron"] = MapColor.Iron,
        ["foliage"] = MapColor.Foliage,
        ["snow"] = MapColor.Snow,
        ["clay"] = MapColor.Clay,
        ["dirt"] = MapColor.Dirt,
        ["stone"] = MapColor.Stone,
        ["water"] = MapColor.Water,
        ["wood"] = MapColor.Wood
    }.ToFrozenDictionary();

    /// <summary>Returns the canonical material for <paramref name="key" />. Throws on unknown key.</summary>
    public static Material Get(string key) => s_registry.Get(key);

    public static bool TryGet(string key, [NotNullWhen(true)] out Material? material)
        => s_registry.TryGet(key, out material);

    internal static void Initialize()
    {
        if (s_registry.IsInitialized) return;

        DataAssetLoader<MaterialDefinition> loader = (DataAssetLoader<MaterialDefinition>)RegistryDefinitions.Materials.CreateLoader();
        loader.LoadFromPaths(null, null, null);
        if (loader.HasErrors)
        {
            throw new AssetLoadException(loader.FirstErrorMessage ?? "One or more material definitions failed to load.");
        }

        s_registry.Initialize(loader, CreateMaterial);
    }

    private static Material CreateMaterial(MaterialDefinition definition)
    {
        if (!s_mapColors.TryGetValue(definition.MapColor, out MapColor mapColor))
        {
            throw new AssetLoadException(
                $"Material '{definition.Name}' references unknown map color '{definition.MapColor}'.");
        }

        return new Material
        {
            MapColor = mapColor,
            IsFluid = definition.IsFluid,
            IsSolid = definition.IsSolid,
            BlocksVision = definition.BlocksVision,
            BlocksMovement = definition.BlocksMovement,
            IsBurnable = definition.IsBurnable,
            IsReplaceable = definition.IsReplaceable,
            IsHandHarvestable = definition.IsHandHarvestable,
            IsTransparent = definition.IsTransparent,
            PistonBehavior = definition.PistonBehavior
        };
    }
}
