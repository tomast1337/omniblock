using System.Text.Json;
using BetaSharp.Items;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Tests.Items;

public sealed class ItemJsonDumperTests
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };
    private static readonly HashSet<string> s_alwaysKeepFields = ["ProtocolId", "TextureId"];

    [Fact]
    public void DumpItemDefinitionsToJson()
    {
        if (Environment.GetEnvironmentVariable("DUMP_ITEM_JSON") != "1")
        {
            return;
        }

        _ = Item.ByName("stick").Id;

        string outDir = Path.Combine(FindRepoRoot(), "BetaSharp", "assets", "item");
        Directory.CreateDirectory(outDir);

        JsonElement fullDefaults = JsonSerializer.SerializeToElement(new ItemDefinition { ProtocolId = 0 }, s_options);
        JsonElement defaults = StripAlwaysKeepFields(fullDefaults);
        File.WriteAllText(Path.Combine(outDir, "_defaults.json"), JsonSerializer.Serialize(defaults, s_options));

        int count = 0;
        foreach (ItemDefinition definition in DefaultRegistries.Items)
        {
            ResourceLocation? location = DefaultRegistries.Items.GetKey(definition);
            if (location is null) continue;

            JsonElement full = JsonSerializer.SerializeToElement(definition, s_options);
            JsonElement minimal = JsonMerge.StripDefaults(full, defaults, s_options, s_alwaysKeepFields);

            string path = Path.Combine(outDir, $"{location.Path}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(minimal, s_options));
            count++;
        }

        Assert.True(count > 0, "Expected at least one ItemDefinition to dump.");
    }

    private static JsonElement StripAlwaysKeepFields(JsonElement full)
    {
        var kept = new Dictionary<string, JsonElement>();
        foreach (JsonProperty property in full.EnumerateObject())
        {
            if (!s_alwaysKeepFields.Contains(property.Name))
            {
                kept[property.Name] = property.Value;
            }
        }

        return JsonSerializer.SerializeToElement(kept, s_options);
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
