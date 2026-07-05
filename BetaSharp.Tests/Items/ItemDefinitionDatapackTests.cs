using BetaSharp.Items;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Items;

/// <summary>
/// Verifies <see cref="ItemDefinitionJsonLoader"/> (an explicit-ID <see cref="Registries.Data.DataAssetLoader"/>
/// subclass, wired in via <see cref="RegistryDefinitions.Items"/>) supports the same base + global-datapack +
/// world-datapack layering as GameModes/Recipes — see <see cref="RegistryAccessTests"/> for the pattern this
/// mirrors. Does not touch <c>Item.ITEMS[]</c> — that's still boot-time-only, deliberately out of scope.
/// </summary>
[Collection("RegistryAccess")]
public sealed class ItemDefinitionDatapackTests : IDisposable
{
    private readonly string _tempDir;

    private static readonly RegistryKey<ItemDefinition> s_key = new(ResourceLocation.Parse("test:item"));

    public ItemDefinitionDatapackTests()
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

    private void WriteBaseItem(string name, int protocolId)
    {
        string dir = Path.Combine(_tempDir, "assets", "item");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), $$"""{"ProtocolId": {{protocolId}}}""");
    }

    private void WriteDatapackItem(string packName, string ns, string name, int protocolId)
    {
        string dir = Path.Combine(_tempDir, "datapacks", packName, "data", ns, "item");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), $$"""{"ProtocolId": {{protocolId}}}""");
    }

    private void WriteWorldItem(string packName, string ns, string name, int protocolId)
    {
        string dir = Path.Combine(_tempDir, "world", "datapacks", packName, "data", ns, "item");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), $$"""{"ProtocolId": {{protocolId}}}""");
    }

    private static RegistryDefinition<ItemDefinition> RegisterItemsDefinition() =>
        new(s_key, "item", loaderFactory: (path, locations) => new ItemDefinitionJsonLoader(path, locations));

    [Fact]
    public void Build_loads_base_item()
    {
        WriteBaseItem("ruby", 300);
        RegistryAccess.AddDynamic(RegisterItemsDefinition());

        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir);

        ItemDefinition? def = ra.GetOrThrow(s_key).GetValue(ResourceLocation.Parse("betasharp:ruby"));
        Assert.NotNull(def);
        Assert.Equal(300, def.ProtocolId);
    }

    [Fact]
    public void Build_layers_global_datapack_on_top_of_base()
    {
        WriteBaseItem("ruby", 300);
        WriteDatapackItem("mypack", "betasharp", "sapphire", 301);
        RegistryAccess.AddDynamic(RegisterItemsDefinition());

        RegistryAccess ra = RegistryAccess.Build(basePath: _tempDir, datapackPath: _tempDir);
        IReadableRegistry<ItemDefinition> registry = ra.GetOrThrow(s_key);

        Assert.NotNull(registry.GetValue(ResourceLocation.Parse("betasharp:ruby")));
        Assert.NotNull(registry.GetValue(ResourceLocation.Parse("betasharp:sapphire")));
    }

    [Fact]
    public void WithWorldDatapacks_layers_world_pack_without_touching_server_state()
    {
        WriteBaseItem("ruby", 300);
        WriteWorldItem("worldpack", "betasharp", "topaz", 302);
        RegistryAccess.AddDynamic(RegisterItemsDefinition());

        RegistryAccess server = RegistryAccess.Build(basePath: _tempDir);
        RegistryAccess withWorld = server.WithWorldDatapacks(Path.Combine(_tempDir, "world"));

        Assert.Null(server.GetOrThrow(s_key).GetValue(ResourceLocation.Parse("betasharp:topaz")));
        Assert.NotNull(withWorld.GetOrThrow(s_key).GetValue(ResourceLocation.Parse("betasharp:topaz")));
    }

    [Fact]
    public void OutOfRangeProtocolId_failsLoudly()
    {
        WriteBaseItem("bad_item", 5); // 0-255 is reserved for block-derived items
        RegistryAccess.AddDynamic(RegisterItemsDefinition());

        Assert.Throws<Registries.Data.AssetLoadException>(() => RegistryAccess.Build(basePath: _tempDir));
    }
}
