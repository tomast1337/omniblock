using System.Collections.Frozen;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Gen.Chunks;
using OmniBlock.Worlds.Gen.Flat;

namespace OmniBlock.Worlds.Generation;

public readonly record struct WorldGeneratorBuildContext(
    IWorldContext World,
    long Seed,
    string Options);

public readonly record struct WorldGeneratorCompileContext(IBlockRuntimeView Blocks);

public interface ICompiledWorldGenerator
{
    IChunkSource Create(in WorldGeneratorBuildContext context);
}

public interface IWorldGeneratorProvider
{
    ICompiledWorldGenerator Compile(
        ResourceLocation worldTypeId,
        JsonElement definition,
        in WorldGeneratorCompileContext context);
}

public interface IWorldGeneratorProviderRegistry
{
    ICompiledWorldGenerator Compile(
        ResourceLocation providerType,
        ResourceLocation worldTypeId,
        JsonElement definition,
        in WorldGeneratorCompileContext context);
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

    public ICompiledWorldGenerator Compile(
        ResourceLocation providerType,
        ResourceLocation worldTypeId,
        JsonElement definition,
        in WorldGeneratorCompileContext context) =>
        _providers.TryGetValue(providerType, out var provider)
            ? provider.Compile(worldTypeId, definition, context)
            : throw new KeyNotFoundException($"Unknown world-generator provider '{providerType}'.");

    public bool Contains(ResourceLocation providerType) => _providers.ContainsKey(providerType);
}

public static class BuiltInWorldGeneratorProviders
{
    public static readonly ResourceLocation Overworld = "omniblock:overworld";
    public static readonly ResourceLocation Flat = "omniblock:flat";
    public static readonly ResourceLocation Sky = "omniblock:sky";
    public static readonly ResourceLocation Nether = "omniblock:nether";

    public static WorldGeneratorProviderRegistry CreateRegistry() => new(
    [
        Pair(Overworld, new OverworldProvider()),
        Pair(Flat, new FlatProvider()),
        Pair(Sky, new SkyProvider()),
        Pair(Nether, new NetherProvider())
    ]);

    private static KeyValuePair<ResourceLocation, IWorldGeneratorProvider> Pair(
        ResourceLocation type,
        IWorldGeneratorProvider provider) => new(type, provider);

    private sealed class OverworldProvider : IWorldGeneratorProvider
    {
        public ICompiledWorldGenerator Compile(
            ResourceLocation worldTypeId,
            JsonElement definition,
            in WorldGeneratorCompileContext context)
        {
            var references = ReadBlockReferences(worldTypeId, "overworld", definition);
            if (references is null) return new Compiled(null);
            var blocks = OverworldChunkGenerator.BlockIds.Resolve(
                context.Blocks,
                references,
                worldTypeId);
            return new Compiled(blocks);
        }

        private sealed class Compiled(OverworldChunkGenerator.BlockIds? blocks)
            : ICompiledWorldGenerator
        {
            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new OverworldChunkGenerator(context.World, context.Seed)
                    : new OverworldChunkGenerator(context.World, context.Seed, blocks);
        }
    }

    private sealed class SkyProvider : IWorldGeneratorProvider
    {
        public ICompiledWorldGenerator Compile(
            ResourceLocation worldTypeId,
            JsonElement definition,
            in WorldGeneratorCompileContext context)
        {
            var references = ReadBlockReferences(worldTypeId, "sky", definition);
            if (references is null) return new Compiled(null);
            var blocks = SkyChunkGenerator.BlockIds.Resolve(context.Blocks, references, worldTypeId);
            return new Compiled(blocks);
        }

        private sealed class Compiled(SkyChunkGenerator.BlockIds? blocks) : ICompiledWorldGenerator
        {
            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new SkyChunkGenerator(context.World, context.Seed)
                    : new SkyChunkGenerator(context.World, context.Seed, blocks);
        }
    }

    private sealed class FlatProvider : IWorldGeneratorProvider
    {
        public ICompiledWorldGenerator Compile(
            ResourceLocation worldTypeId,
            JsonElement definition,
            in WorldGeneratorCompileContext context)
        {
            var references = ReadBlockReferences(worldTypeId, "flat", definition);
            if (references is null) return new Compiled(null);
            var blocks = FlatChunkGenerator.BlockIds.Resolve(context.Blocks, references, worldTypeId);
            return new Compiled(blocks);
        }

        private sealed class Compiled(FlatChunkGenerator.BlockIds? blocks) : ICompiledWorldGenerator
        {
            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new FlatChunkGenerator(context.World, context.Options)
                    : new FlatChunkGenerator(context.World, context.Options, blocks);
        }
    }

    private sealed class NetherProvider : IWorldGeneratorProvider
    {
        public ICompiledWorldGenerator Compile(
            ResourceLocation profileId,
            JsonElement definition,
            in WorldGeneratorCompileContext context)
        {
            var references = ReadBlockReferences(
                profileId,
                "nether",
                definition,
                "Dimension generator profile");
            if (references is null) return new Compiled(null);
            var blocks = NetherChunkGenerator.BlockIds.Resolve(context.Blocks, references, profileId);
            return new Compiled(blocks);
        }

        private sealed class Compiled(NetherChunkGenerator.BlockIds? blocks) : ICompiledWorldGenerator
        {
            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new NetherChunkGenerator(context.World, context.Seed)
                    : new NetherChunkGenerator(context.World, context.Seed, blocks);
        }
    }

    private static IReadOnlyDictionary<string, string>? ReadBlockReferences(
        ResourceLocation owner,
        string providerName,
        JsonElement definition,
        string ownerKind = "World type")
    {
        if (definition.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null; // Compatibility for programmatically-created legacy definitions.
        if (definition.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"{ownerKind} '{owner}' {providerName} generator settings must be an object.");
        if (!definition.TryGetProperty("Blocks", out var blocksElement)
            || blocksElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"{ownerKind} '{owner}' {providerName} generator settings require a 'Blocks' object.");

        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in blocksElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException(
                    $"{ownerKind} '{owner}' {providerName} block role '{property.Name}' must be a resource name.");
            if (!references.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidOperationException(
                    $"{ownerKind} '{owner}' {providerName} repeats block role '{property.Name}'.");
        }

        return references;
    }
}
