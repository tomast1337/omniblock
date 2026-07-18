namespace BetaSharp;

public sealed record ToolMaterial(string Name, int MaxUses, float Efficiency, int DamageBonus, int HarvestLevel);

public static class ToolMaterialRegistry
{
    private static readonly Dictionary<string, ToolMaterial> s_materials = [];

    internal static void LoadFrom(IEnumerable<ToolMaterialDefinition> definitions)
    {
        s_materials.Clear();
        foreach (ToolMaterialDefinition def in definitions)
        {
            s_materials[def.Name] = new ToolMaterial(def.Name, def.MaxUses, def.Efficiency, def.DamageBonus, def.HarvestLevel);
        }
    }

    public static ToolMaterial Get(string name) => s_materials[name];
}
