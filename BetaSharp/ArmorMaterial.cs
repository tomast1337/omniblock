using System.Text.Json;

namespace BetaSharp;

public sealed record ArmorMaterial(string Name, int ArmorLevel, int RenderIndex);

public static class ArmorMaterialRegistry
{
    private static readonly JsonSerializerOptions s_options = new();
    private static readonly Dictionary<string, ArmorMaterial> s_materials = [];

    internal static void LoadFrom(string assetsPath)
    {
        s_materials.Clear();

        string dir = Path.Combine(assetsPath, "armor_material", "betasharp");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return;
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            ArmorMaterialDefinition def = JsonSerializer.Deserialize<ArmorMaterialDefinition>(File.ReadAllText(file), s_options)
                ?? throw new InvalidOperationException($"Failed to parse armor material from '{file}'.");

            string name = Path.GetFileNameWithoutExtension(file);
            s_materials[name] = new ArmorMaterial(name, def.ArmorLevel, def.RenderIndex);
        }
    }

    public static ArmorMaterial Get(string name) => s_materials[name];
}
