using System.Collections.Frozen;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Gen.Chunks;
using OmniBlock.Worlds.Gen.Flat;
using OmniBlock.Worlds.Generation.Biomes;

namespace OmniBlock.Worlds.Generation;

public readonly record struct WorldGeneratorBuildContext(
    IWorldContext World,
    long Seed,
    string Options);

public readonly record struct WorldGeneratorCompileContext(
    IBlockRuntimeView Blocks,
    RuntimeBiomeGenerationRegistry? BiomeGeneration = null);

public enum InactiveDecorationPolicy
{
    Unsupported,
    DeterministicSerial
}

public interface ICompiledWorldGenerator
{
    InactiveDecorationPolicy InactiveDecorationPolicy => InactiveDecorationPolicy.Unsupported;
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
        {
            if (!staged.TryAdd(type, provider))
                throw new InvalidOperationException($"Duplicate world-generator provider '{type}'.");
        }

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

    private static IReadOnlyDictionary<string, string>? ReadBlockReferences(
        ResourceLocation owner,
        string providerName,
        JsonElement definition,
        string ownerKind = "World type")
    {
        if (definition.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null; // Compatibility for programmatically-created legacy definitions.
        if (definition.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"{ownerKind} '{owner}' {providerName} generator settings must be an object.");
        }

        if (!definition.TryGetProperty("Blocks", out var blocksElement)
            || blocksElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"{ownerKind} '{owner}' {providerName} generator settings require a 'Blocks' object.");
        }

        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in blocksElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"{ownerKind} '{owner}' {providerName} block role '{property.Name}' must be a resource name.");
            }

            if (!references.TryAdd(property.Name, property.Value.GetString()!))
            {
                throw new InvalidOperationException(
                    $"{ownerKind} '{owner}' {providerName} repeats block role '{property.Name}'.");
            }
        }

        return references;
    }

    private sealed class OverworldProvider : IWorldGeneratorProvider
    {
        public ICompiledWorldGenerator Compile(
            ResourceLocation worldTypeId,
            JsonElement definition,
            in WorldGeneratorCompileContext context)
        {
            var references = ReadBlockReferences(worldTypeId, "overworld", definition);
            var settings = ParseSettings(worldTypeId, definition, context.BiomeGeneration);
            if (references is null) return new Compiled(null, settings);
            var blocks = OverworldChunkGenerator.BlockIds.Resolve(
                context.Blocks,
                references,
                worldTypeId);
            return new Compiled(blocks, settings);
        }

        private static OverworldChunkGenerator.Settings ParseSettings(
            ResourceLocation worldTypeId,
            JsonElement definition,
            RuntimeBiomeGenerationRegistry? biomeGeneration)
        {
            var parsed = definition.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? new SettingsDefinition()
                : definition.Deserialize<SettingsDefinition>() ?? new SettingsDefinition();
            OverworldChunkGenerator.Settings settings = new(
                parsed.MinLimitOctaves, parsed.MaxLimitOctaves, parsed.SelectorOctaves,
                parsed.SurfaceOctaves, parsed.DepthOctaves, parsed.FloatingScaleOctaves,
                parsed.FloatingNoiseOctaves, parsed.ForestOctaves, parsed.HorizontalNoiseScale,
                parsed.VerticalNoiseScale, parsed.DungeonAttempts, parsed.ClayAttempts,
                parsed.DirtAttempts, parsed.GravelAttempts, parsed.CoalAttempts, parsed.IronAttempts,
                parsed.SurfaceLevel, parsed.SurfaceNoiseScale, parsed.BedrockDepth,
                parsed.SandstoneDepthBound,
                parsed.Features,
                biomeGeneration ?? RuntimeBiomeGenerationRegistry.Empty);
            settings.Validate(worldTypeId);
            return settings;
        }

        private sealed class SettingsDefinition
        {
            public int MinLimitOctaves { get; init; } = 16;
            public int MaxLimitOctaves { get; init; } = 16;
            public int SelectorOctaves { get; init; } = 8;
            public int SurfaceOctaves { get; init; } = 4;
            public int DepthOctaves { get; init; } = 4;
            public int FloatingScaleOctaves { get; init; } = 10;
            public int FloatingNoiseOctaves { get; init; } = 16;
            public int ForestOctaves { get; init; } = 8;
            public double HorizontalNoiseScale { get; init; } = 684.412D;
            public double VerticalNoiseScale { get; init; } = 684.412D;
            public int DungeonAttempts { get; init; } = 8;
            public int ClayAttempts { get; init; } = 10;
            public int DirtAttempts { get; init; } = 20;
            public int GravelAttempts { get; init; } = 10;
            public int CoalAttempts { get; init; } = 20;
            public int IronAttempts { get; init; } = 20;
            public int SurfaceLevel { get; init; } = 64;
            public double SurfaceNoiseScale { get; init; } = 1.0D / 32.0D;
            public int BedrockDepth { get; init; } = 5;
            public int SandstoneDepthBound { get; init; } = 4;

            public OverworldChunkGenerator.FeatureSettings Features { get; init; } =
                OverworldChunkGenerator.FeatureSettings.Default;
        }

        private sealed class Compiled(
            OverworldChunkGenerator.BlockIds? blocks,
            OverworldChunkGenerator.Settings settings)
            : ICompiledWorldGenerator
        {
            public InactiveDecorationPolicy InactiveDecorationPolicy =>
                InactiveDecorationPolicy.DeterministicSerial;

            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new OverworldChunkGenerator(
                        context.World,
                        context.Seed,
                        OverworldChunkGenerator.BlockIds.Resolve(context.World.Content.Blocks),
                        settings)
                    : new OverworldChunkGenerator(context.World, context.Seed, blocks, settings);
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
            var settings = ParseSettings(worldTypeId, definition);
            if (references is null) return new Compiled(null, settings);
            var blocks = SkyChunkGenerator.BlockIds.Resolve(context.Blocks, references, worldTypeId);
            return new Compiled(blocks, settings);
        }

        private static SkyChunkGenerator.Settings ParseSettings(
            ResourceLocation worldTypeId,
            JsonElement definition)
        {
            var parsed = definition.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? new SettingsDefinition()
                : definition.Deserialize<SettingsDefinition>() ?? new SettingsDefinition();
            SkyChunkGenerator.Settings settings = new(
                parsed.MinLimitOctaves, parsed.MaxLimitOctaves, parsed.SelectorOctaves,
                parsed.DepthOctaves, parsed.FloatingScaleOctaves, parsed.FloatingNoiseOctaves,
                parsed.ForestOctaves, parsed.HorizontalNoiseScale, parsed.VerticalNoiseScale,
                parsed.DungeonAttempts, parsed.ClayAttempts, parsed.DirtAttempts,
                parsed.GravelAttempts, parsed.CoalAttempts, parsed.IronAttempts,
                parsed.SurfaceNoiseScale, parsed.SandstoneDepthBound, parsed.Features);
            settings.Validate(worldTypeId);
            return settings;
        }

        private sealed class SettingsDefinition
        {
            public int MinLimitOctaves { get; init; } = 16;
            public int MaxLimitOctaves { get; init; } = 16;
            public int SelectorOctaves { get; init; } = 8;
            public int DepthOctaves { get; init; } = 4;
            public int FloatingScaleOctaves { get; init; } = 10;
            public int FloatingNoiseOctaves { get; init; } = 16;
            public int ForestOctaves { get; init; } = 8;
            public double HorizontalNoiseScale { get; init; } = 684.412D;
            public double VerticalNoiseScale { get; init; } = 684.412D;
            public int DungeonAttempts { get; init; } = 8;
            public int ClayAttempts { get; init; } = 10;
            public int DirtAttempts { get; init; } = 20;
            public int GravelAttempts { get; init; } = 10;
            public int CoalAttempts { get; init; } = 20;
            public int IronAttempts { get; init; } = 20;
            public double SurfaceNoiseScale { get; init; } = 1.0D / 32.0D;
            public int SandstoneDepthBound { get; init; } = 4;

            public SkyChunkGenerator.FeatureSettings Features { get; init; } =
                SkyChunkGenerator.FeatureSettings.Default;
        }

        private sealed class Compiled(
            SkyChunkGenerator.BlockIds? blocks,
            SkyChunkGenerator.Settings settings) : ICompiledWorldGenerator
        {
            public InactiveDecorationPolicy InactiveDecorationPolicy =>
                InactiveDecorationPolicy.DeterministicSerial;

            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new SkyChunkGenerator(
                        context.World,
                        context.Seed,
                        SkyChunkGenerator.BlockIds.Resolve(context.World.Content.Blocks),
                        settings)
                    : new SkyChunkGenerator(context.World, context.Seed, blocks, settings);
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
            var settings = ParseSettings(worldTypeId, definition);
            if (references is null) return new Compiled(null, settings);
            var blocks = FlatChunkGenerator.BlockIds.Resolve(context.Blocks, references, worldTypeId);
            return new Compiled(blocks, settings);
        }

        private static FlatChunkGenerator.Settings ParseSettings(
            ResourceLocation worldTypeId,
            JsonElement definition)
        {
            var parsed = definition.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? new SettingsDefinition()
                : definition.Deserialize<SettingsDefinition>() ?? new SettingsDefinition();
            FlatChunkGenerator.Settings settings = new(
                parsed.DungeonAttempts,
                parsed.ClayAttempts,
                parsed.DirtAttempts,
                parsed.GravelAttempts,
                parsed.CoalAttempts,
                parsed.IronAttempts,
                parsed.Features);
            settings.Validate(worldTypeId);
            return settings;
        }

        private sealed class SettingsDefinition
        {
            public int DungeonAttempts { get; init; } = 8;
            public int ClayAttempts { get; init; } = 10;
            public int DirtAttempts { get; init; } = 20;
            public int GravelAttempts { get; init; } = 10;
            public int CoalAttempts { get; init; } = 20;
            public int IronAttempts { get; init; } = 20;

            public FlatChunkGenerator.FeatureSettings Features { get; init; } =
                FlatChunkGenerator.FeatureSettings.Default;
        }

        private sealed class Compiled(
            FlatChunkGenerator.BlockIds? blocks,
            FlatChunkGenerator.Settings settings) : ICompiledWorldGenerator
        {
            public InactiveDecorationPolicy InactiveDecorationPolicy =>
                InactiveDecorationPolicy.DeterministicSerial;

            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new FlatChunkGenerator(
                        context.World,
                        context.Options,
                        FlatChunkGenerator.BlockIds.Resolve(context.World.Content.Blocks),
                        settings)
                    : new FlatChunkGenerator(context.World, context.Options, blocks, settings);
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
            var settings = ParseSettings(profileId, definition);
            if (references is null) return new Compiled(null, settings);
            var blocks = NetherChunkGenerator.BlockIds.Resolve(context.Blocks, references, profileId);
            return new Compiled(blocks, settings);
        }

        private static NetherChunkGenerator.Settings ParseSettings(
            ResourceLocation profileId,
            JsonElement definition)
        {
            var parsed = definition.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? new NetherSettingsDefinition()
                : definition.Deserialize<NetherSettingsDefinition>()
                  ?? throw new InvalidOperationException(
                      $"Dimension generator profile '{profileId}' has invalid nether settings.");
            NetherChunkGenerator.Settings settings = new(
                parsed.LavaLevel,
                parsed.SurfaceLevel,
                parsed.SurfaceNoiseScale,
                parsed.HorizontalNoiseScale,
                parsed.VerticalNoiseScale,
                parsed.MinLimitOctaves,
                parsed.MaxLimitOctaves,
                parsed.SelectorOctaves,
                parsed.SurfaceOctaves,
                parsed.SurfaceDepthOctaves,
                parsed.ScaleOctaves,
                parsed.DepthOctaves,
                parsed.LavaSpringAttempts,
                parsed.GlowstoneClusterAttempts,
                parsed.BedrockDepth,
                parsed.FeatureHorizontalRange,
                parsed.FeatureHorizontalOffset,
                parsed.FeatureUpperY,
                parsed.FeatureVerticalOffset,
                parsed.FireClusterBound,
                parsed.GlowstoneClusterBound,
                parsed.RareGlowstoneUpperY,
                parsed.MushroomChance,
                parsed.MushroomUpperY);
            settings.Validate(profileId);
            return settings;
        }

        private sealed class NetherSettingsDefinition
        {
            public int LavaLevel { get; init; } = 32;
            public int SurfaceLevel { get; init; } = 64;
            public double SurfaceNoiseScale { get; init; } = 1.0D / 32.0D;
            public double HorizontalNoiseScale { get; init; } = 684.412D;
            public double VerticalNoiseScale { get; init; } = 2053.236D;
            public int MinLimitOctaves { get; init; } = 16;
            public int MaxLimitOctaves { get; init; } = 16;
            public int SelectorOctaves { get; init; } = 8;
            public int SurfaceOctaves { get; init; } = 4;
            public int SurfaceDepthOctaves { get; init; } = 4;
            public int ScaleOctaves { get; init; } = 10;
            public int DepthOctaves { get; init; } = 16;
            public int LavaSpringAttempts { get; init; } = 8;
            public int GlowstoneClusterAttempts { get; init; } = 10;
            public int BedrockDepth { get; init; } = 5;
            public int FeatureHorizontalRange { get; init; } = 16;
            public int FeatureHorizontalOffset { get; init; } = 8;
            public int FeatureUpperY { get; init; } = 120;
            public int FeatureVerticalOffset { get; init; } = 4;
            public int FireClusterBound { get; init; } = 10;
            public int GlowstoneClusterBound { get; init; } = 10;
            public int RareGlowstoneUpperY { get; init; } = 128;
            public int MushroomChance { get; init; } = 1;
            public int MushroomUpperY { get; init; } = 128;
        }

        private sealed class Compiled(
            NetherChunkGenerator.BlockIds? blocks,
            NetherChunkGenerator.Settings settings) : ICompiledWorldGenerator
        {
            public InactiveDecorationPolicy InactiveDecorationPolicy =>
                InactiveDecorationPolicy.DeterministicSerial;

            public IChunkSource Create(in WorldGeneratorBuildContext context) =>
                blocks is null
                    ? new NetherChunkGenerator(
                        context.World,
                        context.Seed,
                        NetherChunkGenerator.BlockIds.Resolve(context.World.Content.Blocks),
                        settings)
                    : new NetherChunkGenerator(context.World, context.Seed, blocks, settings);
        }
    }
}
