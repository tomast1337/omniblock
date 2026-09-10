using System.Collections.Frozen;
using System.Text.Json;
using OmniBlock.Registries.Data;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Worlds.Generation;

/// <summary>
///     Data-owned generator selection for a dimension whose terrain is not selected by a world
///     preset. Provider-specific settings remain an opaque JSON envelope until compilation.
/// </summary>
public sealed class DimensionGeneratorProfileDefinition : DataAsset
{
    public int DimensionId { get; init; }
    public string Generator { get; init; } = "";
    public JsonElement GeneratorSettings { get; init; }
}

public sealed class DimensionGeneratorProfile
{
    private readonly ICompiledWorldGenerator _generator;

    internal DimensionGeneratorProfile(
        ResourceLocation key,
        int dimensionId,
        ResourceLocation generatorProviderType,
        ICompiledWorldGenerator generator)
    {
        Key = key;
        DimensionId = dimensionId;
        GeneratorProviderType = generatorProviderType;
        _generator = generator;
    }

    public ResourceLocation Key { get; }
    public int DimensionId { get; }
    public ResourceLocation GeneratorProviderType { get; }

    internal IChunkSource CreateGenerator(in WorldGeneratorBuildContext context) =>
        _generator.Create(context);
}

public sealed class RuntimeDimensionGeneratorProfileRegistry
{
    private readonly FrozenDictionary<int, DimensionGeneratorProfile> _byDimensionId;
    private readonly FrozenDictionary<ResourceLocation, DimensionGeneratorProfile> _byKey;

    internal RuntimeDimensionGeneratorProfileRegistry(IEnumerable<DimensionGeneratorProfile> profiles)
    {
        var byKey = new Dictionary<ResourceLocation, DimensionGeneratorProfile>();
        var byDimensionId = new Dictionary<int, DimensionGeneratorProfile>();
        foreach (var profile in profiles)
        {
            if (!byKey.TryAdd(profile.Key, profile))
            {
                throw new InvalidOperationException(
                    $"Duplicate dimension generator profile '{profile.Key}'.");
            }

            if (!byDimensionId.TryAdd(profile.DimensionId, profile))
            {
                throw new InvalidOperationException(
                    $"Duplicate dimension generator profile id {profile.DimensionId} for '{profile.Key}'.");
            }
        }

        _byKey = byKey.ToFrozenDictionary();
        _byDimensionId = byDimensionId.ToFrozenDictionary();
    }

    public IReadOnlyCollection<DimensionGeneratorProfile> Values => _byKey.Values;

    public DimensionGeneratorProfile Get(ResourceLocation key) =>
        _byKey.TryGetValue(key, out var profile)
            ? profile
            : throw new KeyNotFoundException($"Unknown dimension generator profile '{key}'.");

    public DimensionGeneratorProfile GetByDimensionId(int dimensionId) =>
        _byDimensionId.TryGetValue(dimensionId, out var profile)
            ? profile
            : throw new KeyNotFoundException(
                $"Unknown dimension generator profile for dimension id {dimensionId}.");
}
