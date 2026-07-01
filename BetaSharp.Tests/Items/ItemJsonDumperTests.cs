using System.Text.Json;
using BetaSharp.Items;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Items;

public sealed class ItemJsonDumperTests
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    [Fact]
    public void DumpItemDefinitionsToJson()
    {
        if (Environment.GetEnvironmentVariable("DUMP_ITEM_JSON") != "1")
        {
            return;
        }

        _ = Item.ByName("stick").id;

        string outDir = Path.Combine(FindRepoRoot(), "BetaSharp", "assets", "item", "betasharp");
        Directory.CreateDirectory(outDir);

        int count = 0;
        foreach (ItemDefinition definition in DefaultRegistries.Items)
        {
            ResourceLocation? location = DefaultRegistries.Items.GetKey(definition);
            if (location is null) continue;

            string path = Path.Combine(outDir, $"{location.Path}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(definition, s_options));
            count++;
        }

        Assert.True(count > 0, "Expected at least one ItemDefinition to dump.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root (no .git ancestor found).");
    }
}
