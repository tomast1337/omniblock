using OmniBlock.Registries;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Generation;

namespace OmniBlock.Worlds;

public class WorldType
{
    public static readonly WorldType Default = new("omniblock:default", BuiltInWorldGeneratorProviders.Overworld, "/gui/world_types/default.png");
    public static readonly WorldType Flat = new("omniblock:flat", BuiltInWorldGeneratorProviders.Flat, "/gui/world_types/flat.png");
    public static readonly WorldType Sky = new("omniblock:sky", BuiltInWorldGeneratorProviders.Sky, "/gui/world_types/sky.png");

    internal WorldType(
        ResourceLocation key,
        ResourceLocation generatorProviderType,
        string iconPath = "",
        bool canBeCreated = true,
        ICompiledWorldGenerator? compiledGenerator = null)
    {
        Key = key;
        GeneratorProviderType = generatorProviderType;
        IconPath = iconPath;
        CanBeCreated = canBeCreated;
        CompiledGenerator = compiledGenerator;
    }

    internal static IReadOnlyList<WorldType> BuiltIns { get; } = [Default, Flat, Sky];

    public ResourceLocation Key { get; }
    public ResourceLocation GeneratorProviderType { get; }
    public string Name => Key.Path;
    public string IconPath { get; }
    public bool CanBeCreated { get; }
    internal ICompiledWorldGenerator? CompiledGenerator { get; }

    internal IChunkSource CreateGenerator(in WorldGeneratorBuildContext context) =>
        CompiledGenerator?.Create(context)
        ?? throw new InvalidOperationException($"World type '{Key}' has not been compiled into a content runtime.");

    public string GetTranslateName() => $"generator.{Name}";

    public static WorldType ParseWorldType(string name)
    {
        if (ContentRuntime.TryGetCurrent(out var runtime) &&
            runtime!.WorldTypes.TryGet(name, out var runtimeType)) return runtimeType;

        foreach (var type in BuiltIns)
        {
            if (type.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                type.Key.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)) return type;
        }

        return Default;
    }
}
