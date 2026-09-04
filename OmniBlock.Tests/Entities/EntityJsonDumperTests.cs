using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests.Entities;

/// <summary>
///     One-shot generator for <c>OmniBlock/assets/entity/*.json</c>, mirroring
///     <c>ItemJsonDumperTests</c>. Env-var gated and a no-op in normal runs; exists so the files are
///     generated from live data rather than transcribed by hand.
///     Run with <c>DUMP_ENTITY_JSON=1 dotnet test --filter FullyQualifiedName~EntityJsonDumperTests</c>.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityJsonDumperTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> s_alwaysKeepFields = ["ProtocolId"];

    [Fact]
    public void DumpEntityDefinitionsToJson()
    {
        if (Environment.GetEnvironmentVariable("DUMP_ENTITY_JSON") != "1")
        {
            return;
        }

        var outDir = Path.Combine(FindRepoRoot(), "OmniBlock", "assets", "entity");
        Directory.CreateDirectory(outDir);

        var fullDefaults = JsonSerializer.SerializeToElement(EntityDefinition.Default, s_options);
        var defaults = StripAlwaysKeepFields(fullDefaults);
        File.WriteAllText(Path.Combine(outDir, "_defaults.json"), JsonSerializer.Serialize(defaults, s_options));

        var count = 0;
        foreach (var type in EntityRegistryDefinitionTests.MobTypes)
        {
            var definition = type.RequireDefinition();
            var full = JsonSerializer.SerializeToElement(definition, s_options);
            var minimal = JsonMerge.StripDefaults(full, defaults, s_options, s_alwaysKeepFields);

            File.WriteAllText(Path.Combine(outDir, $"{type.Id.ToLowerInvariant()}.json"), JsonSerializer.Serialize(minimal, s_options));
            count++;
        }

        Assert.Equal(EntityRegistryDefinitionTests.MobTypes.Length, count);
    }

    private static JsonElement StripAlwaysKeepFields(JsonElement full)
    {
        Dictionary<string, JsonElement> kept = [];
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
