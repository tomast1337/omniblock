using System.Collections.Frozen;
using System.Numerics;
using OmniBlock.Blocks;

namespace OmniBlock.Client.Rendering.Blocks.Models;

internal readonly record struct ModelVertex(Vector3 Position, Vector2 Uv);

/// <summary>Local-space geometry only. Lighting, biome tint and neighbor visibility are per mesh.</summary>
internal readonly record struct CompiledBlockQuad(
    ModelVertex A, ModelVertex B, ModelVertex C, ModelVertex D,
    ResourceLocation Texture, int ArrayLayer, Side Direction, Side? CullFace, int TintIndex, bool Shade);

internal sealed class CompiledBlockModel
{
    private readonly CompiledBlockQuad[] _quads;

    internal CompiledBlockModel(ResourceLocation id, bool ambientOcclusion, IEnumerable<CompiledBlockQuad> quads)
    {
        Id = id;
        AmbientOcclusion = ambientOcclusion;
        _quads = quads.ToArray();
    }

    public ResourceLocation Id { get; }
    public bool AmbientOcclusion { get; }
    public ReadOnlySpan<CompiledBlockQuad> Quads => _quads;
}

/// <summary>
/// A complete candidate snapshot. Building never mutates a published snapshot; the future resource
/// reload owner must install this together with the matching texture-array generation.
/// </summary>
internal sealed class BlockModelCatalog
{
    public const int MaximumModels = 1024;
    private readonly FrozenDictionary<ResourceLocation, CompiledBlockModel> _models;

    private BlockModelCatalog(Dictionary<ResourceLocation, CompiledBlockModel> models) => _models = models.ToFrozenDictionary();

    public int Count => _models.Count;
    public CompiledBlockModel Get(ResourceLocation id) => _models.TryGetValue(id, out var model)
        ? model : throw new KeyNotFoundException($"Unknown block model '{id}'.");

    public static BlockModelCatalog Build(IEnumerable<KeyValuePair<ResourceLocation, string>> definitions,
        Func<ResourceLocation, int> resolveTextureLayer)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(resolveTextureLayer);
        var models = new Dictionary<ResourceLocation, CompiledBlockModel>();
        foreach (var (id, json) in definitions)
        {
            if (models.ContainsKey(id)) throw new InvalidDataException($"Block model '{id}': duplicate resource ID.");
            if (models.Count >= MaximumModels) throw new InvalidDataException($"Block model '{id}': catalog exceeds {MaximumModels} models.");
            models.Add(id, BlockModelCompiler.Compile(id, json, resolveTextureLayer));
        }
        return new BlockModelCatalog(models);
    }
}
