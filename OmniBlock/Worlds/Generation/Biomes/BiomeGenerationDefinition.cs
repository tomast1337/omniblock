using System.Collections.Frozen;
using OmniBlock.Registries.Data;

namespace OmniBlock.Worlds.Generation.Biomes;

/// <summary>Biome-owned generation choices consumed by terrain decorators.</summary>
public sealed class BiomeGenerationDefinition : DataAsset
{
    /// <summary>
    ///     Bound passed to the legacy fern-selection random draw. Zero disables fern selection;
    ///     values greater than zero preserve the historical <c>NextInt(bound) != 0</c> behavior.
    /// </summary>
    public int FernSelectionBound { get; init; }
}

public readonly record struct BiomeGenerationSettings(int FernSelectionBound);

public sealed class RuntimeBiomeGenerationRegistry
{
    private readonly FrozenDictionary<ResourceLocation, BiomeGenerationSettings> _settings;

    internal RuntimeBiomeGenerationRegistry(
        IEnumerable<KeyValuePair<ResourceLocation, BiomeGenerationSettings>> settings)
    {
        var staged = new Dictionary<ResourceLocation, BiomeGenerationSettings>();
        foreach (var (key, value) in settings)
            if (!staged.TryAdd(key, value))
                throw new InvalidOperationException($"Duplicate biome generation definition '{key}'.");
        _settings = staged.ToFrozenDictionary();
    }

    internal static RuntimeBiomeGenerationRegistry Empty { get; } = new([]);

    public IReadOnlyCollection<ResourceLocation> Keys => _settings.Keys;

    public BiomeGenerationSettings Get(ResourceLocation key) =>
        _settings.TryGetValue(key, out var settings)
            ? settings
            : throw new KeyNotFoundException($"Unknown biome generation definition '{key}'.");

    public BiomeGenerationSettings GetOrDefault(ResourceLocation key) =>
        _settings.TryGetValue(key, out var settings) ? settings : default;
}
