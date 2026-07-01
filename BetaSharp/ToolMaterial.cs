using System.Text.Json;

namespace BetaSharp;

public sealed record ToolMaterial(string Name, int MaxUses, float Efficiency, int DamageBonus, int HarvestLevel);

public static class ToolMaterialRegistry
{
    private static readonly JsonSerializerOptions s_options = new();
    private static readonly Dictionary<string, ToolMaterial> s_materials = [];

    internal static void LoadFrom(string assetsPath)
    {
        s_materials.Clear();

        string dir = Path.Combine(assetsPath, "item_material", "betasharp");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return;
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            ToolMaterialDefinition def = JsonSerializer.Deserialize<ToolMaterialDefinition>(File.ReadAllText(file), s_options)
                ?? throw new InvalidOperationException($"Failed to parse tool material from '{file}'.");

            string name = Path.GetFileNameWithoutExtension(file);
            s_materials[name] = new ToolMaterial(name, def.MaxUses, def.Efficiency, def.DamageBonus, def.HarvestLevel);
        }
    }

    public static ToolMaterial Get(string name) => s_materials[name];
}
