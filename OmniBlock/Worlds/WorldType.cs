namespace OmniBlock.Worlds;

public class WorldType
{
    public static readonly WorldType Default = new("omniblock:default", "/gui/world_types/default.png");
    public static readonly WorldType Flat = new("omniblock:flat", "/gui/world_types/flat.png");
    public static readonly WorldType Sky = new("omniblock:sky", "/gui/world_types/sky.png");

    internal static IReadOnlyList<WorldType> BuiltIns { get; } = [Default, Flat, Sky];

    internal WorldType(ResourceLocation key, string iconPath = "", bool canBeCreated = true)
    {
        Key = key;
        IconPath = iconPath;
        CanBeCreated = canBeCreated;
    }

    public ResourceLocation Key { get; }
    public string Name => Key.Path;
    public string IconPath { get; }
    public bool CanBeCreated { get; }

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
