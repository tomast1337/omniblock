namespace OmniBlock;

public sealed record ArmorMaterial(string Name, int ArmorLevel, string TexturePrefix);

public static class ArmorMaterialRegistry
{
    private static readonly Dictionary<string, ArmorMaterial> s_materials = [];

    internal static void LoadFrom(IEnumerable<ArmorMaterialDefinition> definitions)
    {
        s_materials.Clear();
        foreach (ArmorMaterialDefinition def in definitions)
        {
            s_materials[def.Name] = new ArmorMaterial(def.Name, def.ArmorLevel, def.TexturePrefix);
        }
    }

    public static ArmorMaterial Get(string name) => s_materials[name];
}
