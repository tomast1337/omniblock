using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;
using OmniBlock.Worlds.Maps;

namespace OmniBlock.Blocks.Materials;

public static class MaterialRegistry
{
    private static readonly CanonicalRegistry<Material> s_registry = new("material");

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

    public static Material Get(string key)
    {
        return s_registry.Get(key);
    }

    public static bool TryGet(string key, [NotNullWhen(true)] out Material? material)
    {
        return s_registry.TryGet(key, out material);
    }

    public static string? TryGetName(Material material)
    {
        return s_registry.TryGetKey(material);
    }

    internal static void Initialize()
    {
        if (s_registry.IsInitialized) return;

        var loader = (DataAssetLoader<MaterialDefinition>)RegistryDefinitions.Materials.CreateLoader();
        loader.LoadFromPaths(null, null, null);
        if (loader.HasErrors) throw new AssetLoadException(loader.FirstErrorMessage ?? "One or more material definitions failed to load.");

        s_registry.Initialize(loader, CreateMaterial);
    }

    private static Material CreateMaterial(MaterialDefinition definition)
    {
        if (!s_mapColors.TryGetValue(definition.MapColor, out var mapColor))
            throw new AssetLoadException(
                $"Material '{definition.Name}' references unknown map color '{definition.MapColor}'.");

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