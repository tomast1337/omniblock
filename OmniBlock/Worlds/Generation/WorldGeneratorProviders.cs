using System.Collections.Frozen;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Gen.Chunks;
using OmniBlock.Worlds.Gen.Flat;

namespace OmniBlock.Worlds.Generation;

public readonly record struct WorldGeneratorBuildContext(
    IWorldContext World,
    long Seed,
    string Options);

public interface IWorldGeneratorProvider
{
    IChunkSource Create(in WorldGeneratorBuildContext context);
}

public interface IWorldGeneratorProviderRegistry
{
    IChunkSource Create(ResourceLocation providerType, in WorldGeneratorBuildContext context);
    bool Contains(ResourceLocation providerType);
}

public sealed class WorldGeneratorProviderRegistry : IWorldGeneratorProviderRegistry
{
    private readonly FrozenDictionary<ResourceLocation, IWorldGeneratorProvider> _providers;

    public WorldGeneratorProviderRegistry(
        IEnumerable<KeyValuePair<ResourceLocation, IWorldGeneratorProvider>> providers)
    {
        var staged = new Dictionary<ResourceLocation, IWorldGeneratorProvider>();
        foreach (var (type, provider) in providers)
            if (!staged.TryAdd(type, provider))
                throw new InvalidOperationException($"Duplicate world-generator provider '{type}'.");
        _providers = staged.ToFrozenDictionary();
    }

    public IChunkSource Create(
        ResourceLocation providerType,
        in WorldGeneratorBuildContext context) =>
        _providers.TryGetValue(providerType, out var provider)
            ? provider.Create(context)
            : throw new KeyNotFoundException($"Unknown world-generator provider '{providerType}'.");

    public bool Contains(ResourceLocation providerType) => _providers.ContainsKey(providerType);
}

public static class BuiltInWorldGeneratorProviders
{
    public static readonly ResourceLocation Overworld = "omniblock:overworld";
    public static readonly ResourceLocation Flat = "omniblock:flat";
    public static readonly ResourceLocation Sky = "omniblock:sky";

    public static WorldGeneratorProviderRegistry CreateRegistry() => new(
    [
        Pair(Overworld, new DelegateProvider(static context =>
            new OverworldChunkGenerator(context.World, context.Seed))),
        Pair(Flat, new DelegateProvider(static context =>
            new FlatChunkGenerator(context.World, context.Options))),
        Pair(Sky, new DelegateProvider(static context =>
            new SkyChunkGenerator(context.World, context.Seed)))
    ]);

    private static KeyValuePair<ResourceLocation, IWorldGeneratorProvider> Pair(
        ResourceLocation type,
        IWorldGeneratorProvider provider) => new(type, provider);

    private sealed class DelegateProvider(
        Func<WorldGeneratorBuildContext, IChunkSource> factory) : IWorldGeneratorProvider
    {
        public IChunkSource Create(in WorldGeneratorBuildContext context) => factory(context);
    }
}
