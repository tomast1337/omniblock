using OmniBlock.Registries;
using OmniBlock.Registries.Data;
using OmniBlock.Entities.Behaviors;

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
    internal static IItemRuntimeView Items { get; private set; } = null!;
    internal static EntityBuildContext BuildContext { get; private set; }
    internal static IEntityBehaviorProviderRegistry BehaviorProviders { get; private set; } = null!;

    internal static IEnumerable<EntityDefinition> All => s_loader ?? Enumerable.Empty<EntityDefinition>();

    internal static void Initialize(ContentRuntimeBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Items = content;
        BuildContext = new EntityBuildContext(content.StagedBlocks, content, new BootstrapEntityTypeView());
        BehaviorProviders = new EntityBehaviorProviderRegistry();
        EntityDefinitionJsonLoader loader = new(RegistryDefinitions.Entities.AssetPath, LoadLocations.Assets);
        loader.LoadFromPaths(null, null, null);
        if (loader.HasErrors)
        {
            throw new AssetLoadException(loader.FirstErrorMessage ?? "Failed to load entity definitions.");
        }

        s_loader = loader;
    }

    private sealed class BootstrapEntityTypeView : IEntityTypeBuildView
    {
        public EntityType Get(ResourceLocation key) => DefaultRegistries.EntityTypes.GetOrThrow(key);
        public bool TryGet(ResourceLocation key, out EntityType? type) =>
            DefaultRegistries.EntityTypes.TryGet(key, out type);
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
