using System.Text.Json;
using OmniBlock.Items;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests.Items;

public sealed class ItemJsonDumperTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> s_alwaysKeepFields = ["ProtocolId", "TextureId"];

    [Fact]
    public void DumpItemDefinitionsToJson()
    {
        if (Environment.GetEnvironmentVariable("DUMP_ITEM_JSON") != "1")
        {
            return;
        }

        _ = ContentRuntime.Current.Items.Get("omniblock:stick").Id;

        var outDir = Path.Combine(FindRepoRoot(), "OmniBlock", "assets", "item");
        Directory.CreateDirectory(outDir);

        var fullDefaults = JsonSerializer.SerializeToElement(new ItemDefinition
        {
            ProtocolId = 0
        }, s_options);
        var defaults = StripAlwaysKeepFields(fullDefaults);
        File.WriteAllText(Path.Combine(outDir, "_defaults.json"), JsonSerializer.Serialize(defaults, s_options));

        var count = 0;
        foreach (var definition in TestItemCatalog.LoadDefinitions())
        {
            ResourceLocation location = new(definition.Namespace, definition.Name);

            var full = JsonSerializer.SerializeToElement(definition, s_options);
            var minimal = JsonMerge.StripDefaults(full, defaults, s_options, s_alwaysKeepFields);

            var path = Path.Combine(outDir, $"{location.Path}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(minimal, s_options));
            count++;
        }

        Assert.True(count > 0, "Expected at least one ItemDefinition to dump.");
    }

    private static JsonElement StripAlwaysKeepFields(JsonElement full)
    {
        var kept = new Dictionary<string, JsonElement>();
        foreach (var property in full.EnumerateObject())
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
