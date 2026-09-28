using System.Collections.Frozen;
using System.Numerics;
using OmniBlock.Blocks;

namespace OmniBlock.Client.Rendering.Blocks.Models;

internal readonly record struct ModelVertex(Vector3 Position, Vector2 Uv);

/// <summary>Local-space geometry only. Lighting, biome tint and neighbor visibility are per mesh.</summary>
internal readonly record struct CompiledBlockQuad(
    ModelVertex A, ModelVertex B, ModelVertex C, ModelVertex D,
    RenderResourceId Texture, int ArrayLayer, Side Direction, Side? CullFace, int TintIndex, bool Shade);

internal sealed class CompiledBlockModel
{
    private readonly CompiledBlockQuad[] _quads;

    internal CompiledBlockModel(RenderResourceId id, bool ambientOcclusion, IEnumerable<CompiledBlockQuad> quads)
    {
        Id = id;
        AmbientOcclusion = ambientOcclusion;
        _quads = quads.ToArray();
    }

    public RenderResourceId Id { get; }
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
    private readonly FrozenDictionary<RenderResourceId, CompiledBlockModel> _models;

    private BlockModelCatalog(Dictionary<RenderResourceId, CompiledBlockModel> models) => _models = models.ToFrozenDictionary();

    public int Count => _models.Count;
    public IEnumerable<RenderResourceId> Ids => _models.Keys;
    public CompiledBlockModel Get(RenderResourceId id) => _models.TryGetValue(id, out var model)
        ? model : throw new KeyNotFoundException($"Unknown block model '{id}'.");

    internal BlockModelCatalog BindTextures(IReadOnlyDictionary<RenderResourceId, int> layers)
    {
        var models = new Dictionary<RenderResourceId, CompiledBlockModel>();
        foreach (var (id, model) in _models)
        {
            var quads = model.Quads.ToArray();
            for (var i = 0; i < quads.Length; i++)
            {
                if (!layers.TryGetValue(quads[i].Texture, out var layer) || layer is < 1 or > 255)
                    throw new InvalidDataException($"Block model '{id}': invalid candidate texture binding '{quads[i].Texture}'.");
                quads[i] = quads[i] with { ArrayLayer = layer };
            }
            models.Add(id, new CompiledBlockModel(id, model.AmbientOcclusion, quads));
        }
        return new BlockModelCatalog(models);
    }

    public static BlockModelCatalog Build(IEnumerable<KeyValuePair<RenderResourceId, string>> definitions,
        Func<RenderResourceId, int> resolveTextureLayer, IEnumerable<RenderResourceId>? entryPoints = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(resolveTextureLayer);
        var sources = new BlockModelDefinitions(definitions);
        var models = new Dictionary<RenderResourceId, CompiledBlockModel>();
        // Explicit entry points allow parameterized parents (e.g. cube with an unbound #all)
        // without pretending that those templates are independently renderable models.
        foreach (var id in entryPoints ?? sources.Ids)
        {
            if (models.ContainsKey(id)) throw new InvalidDataException($"Block model '{id}': duplicate resource ID.");
            if (models.Count >= MaximumModels) throw new InvalidDataException($"Block model '{id}': catalog exceeds {MaximumModels} models.");
            models.Add(id, BlockModelCompiler.Compile(id, sources.Resolve(id), resolveTextureLayer));
        }
        return new BlockModelCatalog(models);
    }
}
