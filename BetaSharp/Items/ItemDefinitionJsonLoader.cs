using System.Text.Json;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Items;

internal static class ItemDefinitionJsonLoader
{
    private const string DefaultsFileName = "_defaults.json";

    private static readonly JsonSerializerOptions s_options = new();

    internal static void LoadInto(IndexedRegistry<ItemDefinition> registry, string assetsPath)
    {
        string dir = Path.Combine(assetsPath, "item", "betasharp");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return;
        }

        JsonElement? defaults = null;
        string defaultsPath = Path.Combine(dir, DefaultsFileName);
        if (File.Exists(defaultsPath))
        {
            defaults = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(defaultsPath), s_options);
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == DefaultsFileName) continue;

            JsonElement raw = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file), s_options);
            JsonElement merged = defaults is { } d ? JsonMerge.Merge(d, raw, s_options) : raw;

            ItemDefinition definition = merged.Deserialize<ItemDefinition>(s_options)
                ?? throw new InvalidOperationException($"Failed to parse item definition from '{file}'.");

            // 0-255 is reserved for block derived items (populated separately from Block's static constructor); Standalone items registered through this loader must live at 256+, within Item.ITEMS' 32000 slots.
            if (definition.ProtocolId is < 256 or >= 32000)
            {
                throw new InvalidOperationException($"Item '{file}' has ProtocolId {definition.ProtocolId}, outside the valid 256-31999 range.");
            }

            string name = Path.GetFileNameWithoutExtension(file);
            var location = new ResourceLocation(Namespace.BetaSharp, name);
            definition.Name = location.Path;
            definition.Namespace = location.Namespace;

            registry.Register(definition.ProtocolId, location, definition);
        }
    }
}
