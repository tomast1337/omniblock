namespace BetaSharp;

public sealed record ArmorMaterial(string Name, int ArmorLevel, int RenderIndex)
{
    public static readonly ArmorMaterial Leather = new("leather", 0, 0);
    public static readonly ArmorMaterial Chain = new("chain", 1, 1);
    public static readonly ArmorMaterial Iron = new("iron", 2, 2);
    public static readonly ArmorMaterial Diamond = new("diamond", 3, 3);
    public static readonly ArmorMaterial Gold = new("gold", 1, 4);
}

public static class ArmorMaterialRegistry
{
    private static readonly Dictionary<string, ArmorMaterial> s_materials = new()
    {
        [ArmorMaterial.Leather.Name] = ArmorMaterial.Leather,
        [ArmorMaterial.Chain.Name] = ArmorMaterial.Chain,
        [ArmorMaterial.Iron.Name] = ArmorMaterial.Iron,
        [ArmorMaterial.Diamond.Name] = ArmorMaterial.Diamond,
        [ArmorMaterial.Gold.Name] = ArmorMaterial.Gold,
    };

    public static ArmorMaterial Get(string name) => s_materials[name];
}
