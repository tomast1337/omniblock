using BetaSharp.Registries.Data;

namespace BetaSharp.Registries;

/// <summary>
/// Describes a data-driven registry: how to create its loader and where its assets live.
/// Register instances with <see cref="RegistryAccess.AddDynamic{T}"/> during bootstrap
/// so that <see cref="RegistryAccess.Build"/> can discover and load them automatically.
/// </summary>
public sealed class RegistryDefinition<T>(
    RegistryKey<T> key,
    string assetPath,
    LoadLocations locations = LoadLocations.AllData,
    bool isReloadable = true, bool serversideOnly = false,
    Func<string, LoadLocations, DataAssetLoader>? loaderFactory = null) where T : class, IDataAsset
{
    public RegistryKey<T> Key { get; } = key;
    internal string AssetPath { get; } = assetPath;
    internal LoadLocations Locations { get; } = locations;

    /// <summary>
    /// When <c>false</c>, this registry is locked after world creation and skipped during
    /// <c>/reload</c>. Use for baked data that cannot safely
    /// change while a world is loaded.
    /// </summary>
    public bool IsReloadable { get; } = isReloadable;

    /// <summary>
    /// Data that is serverside don't need to be synced.
    /// Resource packs are client side only, and don't need to be synced either.
    /// </summary>
    public bool CanSync { get; } = !(serversideOnly || locations == LoadLocations.Resourcepack);

    /// <summary>
    /// Builds this registry's loader — <see cref="DataAssetLoader{T}"/> by default (auto-incrementing IDs), or
    /// <paramref name="loaderFactory"/>'s custom loader when this registry needs explicit numeric IDs instead
    /// (e.g. items — <see cref="DataAssetLoader{T}.GetId"/>() always returns -1, unsuitable for a fixed protocol-ID space).
    /// </summary>
    internal DataAssetLoader CreateLoader() => loaderFactory is not null ? loaderFactory(AssetPath, Locations) : new DataAssetLoader<T>(AssetPath, Locations, !CanSync);
}
