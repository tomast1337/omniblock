using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Build-local source graph. Texture aliases bind only after the entire parent chain merges.</summary>
internal sealed class BlockModelDefinitions
{
    public const int MaximumInheritanceDepth = 32;
    private readonly Dictionary<RenderResourceId, JsonElement> _sources = [];
    public IEnumerable<RenderResourceId> Ids => _sources.Keys;

    public BlockModelDefinitions(IEnumerable<KeyValuePair<RenderResourceId, string>> definitions)
    {
        foreach (var (id, json) in definitions)
        {
            try
            {
                if (_sources.ContainsKey(id)) throw new InvalidDataException("duplicate resource ID");
                if (_sources.Count >= BlockModelCatalog.MaximumModels) throw new InvalidDataException($"catalog exceeds {BlockModelCatalog.MaximumModels} definitions");
                if (json.Length > BlockModelCompiler.MaximumJsonCharacters) throw new InvalidDataException("definition exceeds JSON size limit");
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                BlockModelCompiler.ValidateDefinition(document.RootElement);
                _sources.Add(id, document.RootElement.Clone());
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or FormatException)
            {
                throw new InvalidDataException($"Block model '{id}': {ex.Message}", ex);
            }
        }
        // Validate the complete parent graph, including templates not selected for compilation.
        foreach (var id in _sources.Keys) ValidateChain(id, []);
    }

    public string Resolve(RenderResourceId id) => Merge(id).ToJsonString();

    private void ValidateChain(RenderResourceId id, List<RenderResourceId> chain)
    {
        if (chain.Contains(id)) Fail("parent cycle");
        if (chain.Count >= MaximumInheritanceDepth) Fail($"inheritance exceeds {MaximumInheritanceDepth} models");
        if (!_sources.TryGetValue(id, out var source)) Fail("unknown parent/model");
        chain.Add(id);
        if (source.TryGetProperty("parent", out var parent)) ValidateChain(RenderResourceId.Parse(parent.GetString()!), chain);
        chain.RemoveAt(chain.Count - 1);
        return;

        void Fail(string message) => throw new InvalidDataException($"Block model '{(chain.Count == 0 ? id : chain[0])}': {message}: {string.Join(" -> ", chain.Append(id))}.");
    }

    private JsonObject Merge(RenderResourceId id)
    {
        if (!_sources.TryGetValue(id, out var source)) throw new InvalidDataException($"Unknown block model '{id}'.");
        var result = source.TryGetProperty("parent", out var parent)
            ? Merge(RenderResourceId.Parse(parent.GetString()!)) : new JsonObject();
        foreach (var property in source.EnumerateObject())
        {
            if (property.Name is "parent" or "credit") continue;
            if (property.Name == "textures")
            {
                var merged = result["textures"] as JsonObject ?? new JsonObject();
                foreach (var texture in property.Value.EnumerateObject()) merged[texture.Name] = texture.Value.GetString();
                if (merged.Count > BlockModelCompiler.MaximumTextureVariables)
                    throw new InvalidDataException($"Block model '{id}': inherited texture variables exceed {BlockModelCompiler.MaximumTextureVariables}.");
                if (result["textures"] is null) result["textures"] = merged;
            }
            else
            {
                // elements replace the whole parent list (including explicit []); AO overrides
                // only when supplied, so an omitted child flag inherits rather than resetting.
                result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }
        return result;
    }
}
