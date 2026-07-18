using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Tests.Items;

/// <summary>
/// Verifies tool/armor materials load through the stock <see cref="DataAssetLoader{T}"/> with the
/// same base + global-datapack + world-datapack layering as GameModes/Recipes/Items. Does not touch
/// the boot-time <see cref="ToolMaterialRegistry"/>/<see cref="ArmorMaterialRegistry"/> — items
/// capture their materials at construction, deliberately out of scope (same decision as Item.ITEMS[]).
/// </summary>
[Collection("RegistryAccess")]
public sealed class MaterialDatapackTests : IDisposable
{
    private readonly string _tempDir;

    private static readonly RegistryKey<ToolMaterialDefinition> s_toolKey = new(ResourceLocation.Parse("test:item_material"));
    private static readonly RegistryKey<ArmorMaterialDefinition> s_armorKey = new(ResourceLocation.Parse("test:armor_material"));

    public MaterialDatapackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        RegistryAccess.ClearDynamicEntries();
    }

    public void Dispose()
    {
        RegistryAccess.ClearDynamicEntries();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteMaterial(string relativeDir, string name, string json)
    {
        string dir = Path.Combine(_tempDir, relativeDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), json);
    }

    [Fact]
    public void Build_loads_base_tool_material()
    {
        WriteMaterial(Path.Combine("assets", "item_material"), "copper",
            """{"MaxUses": 100, "Efficiency": 3.5, "DamageBonus": 1, "HarvestLevel": 1}""");
        RegistryAccess.AddDynamic(new RegistryDefinition<ToolMaterialDefinition>(s_toolKey, "item_material"));

        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir);

        ToolMaterialDefinition? def = ra.GetOrThrow(s_toolKey).GetValue(ResourceLocation.Parse("betasharp:copper"));
        Assert.NotNull(def);
        Assert.Equal(100, def.MaxUses);
        Assert.Equal(3.5f, def.Efficiency);
    }

    [Fact]
    public void Build_layers_global_datapack_over_base_tool_material()
    {
        WriteMaterial(Path.Combine("assets", "item_material"), "copper",
            """{"MaxUses": 100, "Efficiency": 3.5, "DamageBonus": 1, "HarvestLevel": 1}""");
        WriteMaterial(Path.Combine("datapacks", "mypack", "data", "betasharp", "item_material"), "copper",
            """{"MaxUses": 500}""");
        RegistryAccess.AddDynamic(new RegistryDefinition<ToolMaterialDefinition>(s_toolKey, "item_material"));

        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir, datapackPath: _tempDir);

        ToolMaterialDefinition? def = ra.GetOrThrow(s_toolKey).GetValue(ResourceLocation.Parse("betasharp:copper"));
        Assert.NotNull(def);
        Assert.Equal(500, def.MaxUses);          // overridden by the datapack
        Assert.Equal(3.5f, def.Efficiency);      // merged from the base file
    }

    [Fact]
    public void WithWorldDatapacks_layers_armor_material_without_touching_server_state()
    {
        WriteMaterial(Path.Combine("assets", "armor_material"), "leather",
            """{"ArmorLevel": 0, "RenderIndex": 0}""");
        WriteMaterial(Path.Combine("world", "datapacks", "worldpack", "data", "betasharp", "armor_material"), "emerald",
            """{"ArmorLevel": 4, "RenderIndex": 4}""");
        RegistryAccess.AddDynamic(new RegistryDefinition<ArmorMaterialDefinition>(s_armorKey, "armor_material"));

        RegistryAccess server = RegistryAccess.Build(basePath: _tempDir);
        RegistryAccess withWorld = server.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        Assert.Null(server.GetOrThrow(s_armorKey).GetValue(ResourceLocation.Parse("betasharp:emerald")));

        ArmorMaterialDefinition? emerald = withWorld.GetOrThrow(s_armorKey).GetValue(ResourceLocation.Parse("betasharp:emerald"));
        Assert.NotNull(emerald);
        Assert.Equal(4, emerald.ArmorLevel);
    }
}
