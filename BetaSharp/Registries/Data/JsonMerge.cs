using System.Text.Json;

namespace BetaSharp.Registries.Data;

/// <summary>
/// Shared JSON object-merge logic used by both <see cref="DataAssetLoader{T}"/> (merging a
/// datapack override on top of a previously-loaded asset) and <see cref="Items.ItemDefinitionJsonLoader"/>
/// (merging an item file on top of <c>_defaults.json</c>).
/// </summary>
internal static class JsonMerge
{
    /// <summary>
    /// Recursively merges <paramref name="overrideObj"/> on top of <paramref name="defaultObj"/>.
    /// Nested objects merge recursively; any other value in the override replaces the default outright.
    /// </summary>
    internal static JsonElement Merge(JsonElement defaultObj, JsonElement overrideObj, JsonSerializerOptions options)
    {
        if (overrideObj.ValueKind != JsonValueKind.Object || defaultObj.ValueKind != JsonValueKind.Object)
        {
            return overrideObj;
        }

        var merged = new Dictionary<string, JsonElement>();

        foreach (JsonProperty property in defaultObj.EnumerateObject())
        {
            merged[property.Name] = property.Value;
        }

        foreach (JsonProperty property in overrideObj.EnumerateObject())
        {
            if (merged.TryGetValue(property.Name, out JsonElement defaultValue) &&
                property.Value.ValueKind == JsonValueKind.Object &&
                defaultValue.ValueKind == JsonValueKind.Object)
            {
                merged[property.Name] = Merge(defaultValue, property.Value, options);
            }
            else
            {
                merged[property.Name] = property.Value;
            }
        }

        return JsonSerializer.SerializeToElement(merged, options);
    }

    /// <summary>
    /// Returns a copy of <paramref name="full"/> with any top-level property whose value exactly
    /// matches <paramref name="defaults"/> removed, except for names in <paramref name="alwaysKeep"/>.
    /// Used to write minimal "overrides only" JSON files.
    /// </summary>
    internal static JsonElement StripDefaults(JsonElement full, JsonElement defaults, JsonSerializerOptions options, IReadOnlySet<string> alwaysKeep)
    {
        var kept = new Dictionary<string, JsonElement>();
        foreach (JsonProperty property in full.EnumerateObject())
        {
            bool matchesDefault = defaults.TryGetProperty(property.Name, out JsonElement defaultValue)
                && property.Value.GetRawText() == defaultValue.GetRawText();

            if (alwaysKeep.Contains(property.Name) || !matchesDefault)
            {
                kept[property.Name] = property.Value;
            }
        }

        return JsonSerializer.SerializeToElement(kept, options);
    }
}
