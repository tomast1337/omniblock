using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetaSharp.Client.Rendering.Entities.Models;

public static class BbModelLoader
{
    private static readonly HashSet<string> s_supportedFormatVersions = ["4.9", "4.10", "5.0"];
    private static readonly Dictionary<string, BbModelDocument> s_cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static string GetEntityModelPath(string entityId) =>
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "assets",
            "models",
            "entities",
            $"{entityId}.bbmodel");

    public static BbModelDocument LoadCached(string entityId)
    {
        lock (s_cache)
        {
            if (s_cache.TryGetValue(entityId, out BbModelDocument? cached))
            {
                return cached;
            }

            BbModelDocument document = Load(entityId);
            s_cache[entityId] = document;
            return document;
        }
    }

    public static BbModelDocument Load(string entityId)
    {
        string path = GetEntityModelPath(entityId);
        return !File.Exists(path) ? throw new FileNotFoundException($".bbmodel not found: {path}", path) : LoadFromFile(path, entityId);
    }

    public static BbModelDocument LoadFromFile(string path, string? entityId = null)
    {
        entityId ??= Path.GetFileNameWithoutExtension(path);

        string json = File.ReadAllText(path);
        using JsonDocument root = JsonDocument.Parse(json);
        JsonElement rootElement = root.RootElement;

        BbModelDocument? document = JsonSerializer.Deserialize<BbModelDocument>(json, s_jsonOptions)
                                    ?? throw new InvalidDataException($"Failed to deserialize .bbmodel '{entityId}'.");

        document.Outliner = ParseOutliner(rootElement);
        Validate(document, entityId);
        return document;
    }

    private static List<BbModelOutlinerEntry> ParseOutliner(JsonElement root)
    {
        List<BbModelOutlinerEntry> result = new();
        if (!root.TryGetProperty("outliner", out JsonElement outliner) || outliner.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement entry in outliner.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            BbModelOutlinerEntry node = new();
            if (entry.TryGetProperty("uuid", out JsonElement uuidProp))
            {
                node.Uuid = uuidProp.GetString() ?? "";
            }

            if (entry.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    if (child.ValueKind == JsonValueKind.String)
                    {
                        string? id = child.GetString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            node.Children.Add(id);
                        }
                    }
                    else if (child.ValueKind == JsonValueKind.Object && child.TryGetProperty("uuid", out JsonElement childUuid))
                    {
                        string? id = childUuid.GetString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            node.Children.Add(id);
                        }
                    }
                }
            }

            result.Add(node);
        }

        return result;
    }

    private static void Validate(BbModelDocument document, string entityId)
    {
        if (document.Meta is null) throw new InvalidDataException($".bbmodel '{entityId}' is missing meta.");

        if (!s_supportedFormatVersions.Contains(document.Meta.FormatVersion)) throw new InvalidDataException($".bbmodel '{entityId}' has unsupported format_version '{document.Meta.FormatVersion}'.");

        if (!string.Equals(document.Meta.ModelFormat, "modded_entity", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($".bbmodel '{entityId}' must use model_format 'modded_entity', got '{document.Meta.ModelFormat}'.");

        if (!document.Meta.BoxUv) throw new InvalidDataException($".bbmodel '{entityId}' must have box_uv enabled.");

        if (document.Elements.Count == 0) throw new InvalidDataException($".bbmodel '{entityId}' has no elements.");

        if (document.Groups.Count == 0) throw new InvalidDataException($".bbmodel '{entityId}' has no groups.");

        if (document.Outliner.Count == 0) throw new InvalidDataException($".bbmodel '{entityId}' has no outliner.");
    }
}
