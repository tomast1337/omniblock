using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BetaSharp;

public class Language(string code, string name, string author)
{
    public string Code { get; } = code;
    public string Name { get; } = name;
    public string Author { get; } = author;
    public bool Unifont { get; set; } = false;

    public IReadOnlyDictionary<string, string>? Translations { get; private set; }

    public void LoadTranslations()
    {
        var asset = AssetManager.Instance.GetAsset($"lang/{Code}.json");
        if (asset == null)
            return;

        using JsonDocument doc = JsonDocument.Parse(asset.GetTextContent());

        Dictionary<string, string> output = new();
        FlattenJson(output, doc.RootElement);
        Translations = output;
    }

    private static void FlattenJson(Dictionary<string, string> output, JsonElement element, string prefix = "")
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    string key = string.IsNullOrEmpty(prefix)
                        ? property.Name
                        : $"{prefix}.{property.Name}";

                    FlattenJson(output, property.Value, key);
                }
                break;

            case JsonValueKind.String:
                output[prefix] = element.GetString() ?? string.Empty;
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                output[prefix] = element.ToString();
                break;
        }
    }

    public string Get(string key)
    {
        if (Translations is null) LoadTranslations();
        // still not loaded, must be error
        if (Translations is null) return key;
        return Translations.TryGetValue(key, out string? translation) ? translation : key;
    }
}
