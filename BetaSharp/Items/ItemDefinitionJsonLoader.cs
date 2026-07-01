using System.Text.Json;
using BetaSharp.Registries;

namespace BetaSharp.Items;

internal static class ItemDefinitionJsonLoader
{
    private static readonly JsonSerializerOptions s_options = new();

    internal static void LoadInto(IndexedRegistry<ItemDefinition> registry, string assetsPath)
    {
        string dir = Path.Combine(assetsPath, "item", "betasharp");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return;
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            ItemDefinition definition = JsonSerializer.Deserialize<ItemDefinition>(File.ReadAllText(file), s_options)
                ?? throw new InvalidOperationException($"Failed to parse item definition from '{file}'.");

            string name = Path.GetFileNameWithoutExtension(file);
            var location = new ResourceLocation(Namespace.BetaSharp, name);
            definition.Name = location.Path;
            definition.Namespace = location.Namespace;

            registry.Register(definition.ProtocolId, location, definition);
        }
    }
}
