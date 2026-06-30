namespace BetaSharp;

public sealed record ToolMaterial(string Name, int MaxUses, float Efficiency, int DamageBonus, int HarvestLevel)
{
    public static readonly ToolMaterial WOOD = new("wood", 59, 2.0f, 0, 0);
    public static readonly ToolMaterial STONE = new("stone", 131, 4.0f, 1, 1);
    public static readonly ToolMaterial IRON = new("iron", 250, 6.0f, 2, 2);
    public static readonly ToolMaterial EMERALD = new("emerald", 1561, 8.0f, 3, 3); // aka Diamond
    public static readonly ToolMaterial GOLD = new("gold", 32, 12.0f, 0, 0);
}

public static class ToolMaterialRegistry
{
    private static readonly Dictionary<string, ToolMaterial> s_materials = new()
    {
        [ToolMaterial.WOOD.Name] = ToolMaterial.WOOD,
        [ToolMaterial.STONE.Name] = ToolMaterial.STONE,
        [ToolMaterial.IRON.Name] = ToolMaterial.IRON,
        [ToolMaterial.EMERALD.Name] = ToolMaterial.EMERALD,
        [ToolMaterial.GOLD.Name] = ToolMaterial.GOLD,
    };

    public static ToolMaterial Get(string name) => s_materials[name];
}
