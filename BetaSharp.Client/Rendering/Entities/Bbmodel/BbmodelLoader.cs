using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetaSharp.Client.Rendering.Entities.Bbmodel;

public static class BbmodelLoader
{
    private static readonly HashSet<string> s_supportedFormatVersions = ["4.9", "4.10", "5.0"];
    private static readonly Dictionary<string, BbmodelDocument> s_cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string GetEntityModelPath(string entityId)
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "assets",
            "models",
            "entities",
            $"{entityId}.bbmodel");
    }

    public static BbmodelDocument LoadCached(string entityId)
    {
        lock (s_cache)
        {
            if (s_cache.TryGetValue(entityId, out BbmodelDocument? cached))
            {
                return cached;
            }

            BbmodelDocument document = Load(entityId);
            s_cache[entityId] = document;
            return document;
        }
    }

    public static BbmodelDocument Load(string entityId)
    {
        string path = GetEntityModelPath(entityId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Bbmodel not found: {path}", path);
        }

        return LoadFromFile(path, entityId);
    }

    public static BbmodelDocument LoadFromFile(string path, string? entityId = null)
    {
        entityId ??= Path.GetFileNameWithoutExtension(path);

        string json = File.ReadAllText(path);
        using JsonDocument root = JsonDocument.Parse(json);
        JsonElement rootElement = root.RootElement;

        BbmodelDocument? document = JsonSerializer.Deserialize<BbmodelDocument>(json, s_jsonOptions)
            ?? throw new InvalidDataException($"Failed to deserialize bbmodel '{entityId}'.");

        document.Outliner = ParseOutliner(rootElement);
        Validate(document, entityId);
        return document;
    }

    private static List<BbmodelOutlinerEntry> ParseOutliner(JsonElement root)
    {
        var result = new List<BbmodelOutlinerEntry>();
        if (!root.TryGetProperty("outliner", out JsonElement outliner) || outliner.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement entry in outliner.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var node = new BbmodelOutlinerEntry();
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

    private static void Validate(BbmodelDocument document, string entityId)
    {
        if (document.Meta is null)
        {
            throw new InvalidDataException($"Bbmodel '{entityId}' is missing meta.");
        }

        if (!s_supportedFormatVersions.Contains(document.Meta.FormatVersion))
        {
            throw new InvalidDataException(
                $"Bbmodel '{entityId}' has unsupported format_version '{document.Meta.FormatVersion}'.");
        }

        if (!string.Equals(document.Meta.ModelFormat, "modded_entity", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Bbmodel '{entityId}' must use model_format 'modded_entity', got '{document.Meta.ModelFormat}'.");
        }

        if (!document.Meta.BoxUv)
        {
            throw new InvalidDataException($"Bbmodel '{entityId}' must have box_uv enabled.");
        }

        if (document.Elements.Count == 0)
        {
            throw new InvalidDataException($"Bbmodel '{entityId}' has no elements.");
        }

        if (document.Groups.Count == 0)
        {
            throw new InvalidDataException($"Bbmodel '{entityId}' has no groups.");
        }

        if (document.Outliner.Count == 0)
        {
            throw new InvalidDataException($"Bbmodel '{entityId}' has no outliner.");
        }
    }
}
