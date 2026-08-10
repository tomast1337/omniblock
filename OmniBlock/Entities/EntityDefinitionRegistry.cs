using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Entities;

/// <summary>
///     Holds the JSON-loaded <see cref="EntityDefinition" />s that <see cref="EntityRegistry" />
///     hands to <see cref="EntityType" /> at registration.
///     <para>
///         Must be initialized before anything touches <see cref="EntityRegistry" />, whose static
///         fields read from here — see <c>DefaultRegistries.Initialize()</c>, which sequences the two.
///     </para>
/// </summary>
public static class EntityDefinitionRegistry
{
    private static EntityDefinitionJsonLoader? s_loader;

    internal static IEnumerable<EntityDefinition> All => s_loader ?? Enumerable.Empty<EntityDefinition>();

    internal static void Initialize()
    {
        EntityDefinitionJsonLoader loader = new(RegistryDefinitions.Entities.AssetPath, LoadLocations.Assets);
        loader.LoadFromPaths(null, null, null);
        if (loader.HasErrors)
        {
            throw new AssetLoadException(loader.FirstErrorMessage ?? "Failed to load entity definitions.");
        }

        s_loader = loader;
    }

    /// <summary>Resolves a definition by registry name (the JSON filename), e.g. <c>"zombie"</c>.</summary>
    internal static EntityDefinition Get(string name)
    {
        if (s_loader is null)
        {
            throw new InvalidOperationException(
                $"{nameof(EntityDefinitionRegistry)} was queried for '{name}' before initialization. " +
                "Entity definitions must load before EntityRegistry's static fields run.");
        }

        return s_loader.Get(new ResourceLocation(Namespace.OmniBlock, name))?.Value
               ?? throw new ArgumentException($"No entity definition found for '{name}'.", nameof(name));
    }
}
