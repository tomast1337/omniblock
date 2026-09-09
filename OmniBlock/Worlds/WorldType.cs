namespace OmniBlock.Worlds;

using OmniBlock.Worlds.Generation;
using OmniBlock.Worlds.Chunks;

public class WorldType
{
    public static readonly WorldType Default = new("omniblock:default", BuiltInWorldGeneratorProviders.Overworld, "/gui/world_types/default.png");
    public static readonly WorldType Flat = new("omniblock:flat", BuiltInWorldGeneratorProviders.Flat, "/gui/world_types/flat.png");
    public static readonly WorldType Sky = new("omniblock:sky", BuiltInWorldGeneratorProviders.Sky, "/gui/world_types/sky.png");

    internal static IReadOnlyList<WorldType> BuiltIns { get; } = [Default, Flat, Sky];

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
        if (Registries.ContentRuntime.TryGetCurrent(out var runtime) &&
            runtime!.WorldTypes.TryGet(name, out var runtimeType)) return runtimeType;

        foreach (var type in BuiltIns)
            if (type.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                type.Key.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)) return type;

        return Default;
    }
}
