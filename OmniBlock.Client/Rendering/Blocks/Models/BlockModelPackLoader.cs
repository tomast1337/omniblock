using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Prepares a CPU-only model candidate without touching active packs, options or GPU resources.
/// Streams returned by the supplied readers are owned and disposed by this loader. Null means
/// absent; exceptions mean broken content and must never silently select a lower-priority source.
/// </summary>
internal static class BlockModelPackLoader
{
    public const int MaximumModelBytes = 1024 * 1024;
    public const int MaximumCatalogBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static BlockModelCatalog Build(IEnumerable<RenderResourceId> entryPoints,
        Func<string, Stream?> openOverride, Func<string, Stream?> openBuiltin,
        Func<RenderResourceId, int> resolveTextureLayer)
    {
        ArgumentNullException.ThrowIfNull(entryPoints);
        ArgumentNullException.ThrowIfNull(openOverride);
        ArgumentNullException.ThrowIfNull(openBuiltin);
        ArgumentNullException.ThrowIfNull(resolveTextureLayer);
        var roots = new HashSet<RenderResourceId>();
        foreach (var id in entryPoints)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (!roots.Add(id)) throw new InvalidDataException($"Block model '{id}': duplicate entry point.");
            if (roots.Count > BlockModelCatalog.MaximumModels) throw new InvalidDataException("Block model entry points exceed catalog limit.");
        }

        var definitions = new Dictionary<RenderResourceId, string>();
        var pending = new Queue<(RenderResourceId Id, RenderResourceId Owner)>();
        foreach (var id in roots) pending.Enqueue((id, id));
        var totalBytes = 0;
        while (pending.TryDequeue(out var request))
        {
            var (id, owner) = request;
            if (definitions.ContainsKey(id)) continue;
            var source = "override";
            try
            {
                if (definitions.Count >= BlockModelCatalog.MaximumModels) throw new InvalidDataException("model dependencies exceed catalog limit");
                var stream = openOverride(id.ModelPath);
                if (stream is null)
                {
                    source = "built-in";
                    stream = openBuiltin(id.ModelPath);
                }
                if (stream is null) throw new InvalidDataException("required model is absent from both override and built-in resources");
                string json;
                using (stream) json = ReadBounded(stream, ref totalBytes);
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
                BlockModelCompiler.ValidateDefinition(document.RootElement);
                definitions.Add(id, json);
                if (document.RootElement.TryGetProperty("parent", out var parent))
                    pending.Enqueue((RenderResourceId.Parse(parent.GetString()!), id));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or FormatException or UnauthorizedAccessException or InvalidOperationException)
            {
                throw new InvalidDataException($"Block model '{owner}' dependency '{id}' ({source} '{id.ModelPath}'): {ex.Message}", ex);
            }
        }
        // Parent graph/cycle checks and final child-bound texture resolution happen before any
        // snapshot can escape. Templates are sources, not independent renderable entry points.
        return BlockModelCatalog.Build(definitions, resolveTextureLayer, roots);
    }

    public static BlockModelCatalog BuildFromZip(string zipPath, IEnumerable<RenderResourceId> entryPoints,
        Func<string, Stream?> openBuiltin, Func<RenderResourceId, int> resolveTextureLayer)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var models = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("assets/", StringComparison.Ordinal) ||
                !entry.FullName.Contains("/models/", StringComparison.Ordinal) ||
                !entry.FullName.EndsWith(".json", StringComparison.Ordinal)) continue;
            if (!models.TryAdd(entry.FullName, entry)) throw new InvalidDataException($"Model pack '{zipPath}': duplicate model entry '{entry.FullName}'.");
            if (models.Count > BlockModelCatalog.MaximumModels) throw new InvalidDataException($"Model pack '{zipPath}': model count exceeds catalog limit.");
        }
        return Build(entryPoints, path =>
        {
            if (!models.TryGetValue(path, out var entry)) return null;
            if (entry.Length > MaximumModelBytes) throw new InvalidDataException("model ZIP entry exceeds byte limit");
            return entry.Open();
        }, openBuiltin, resolveTextureLayer);
    }

    private static string ReadBounded(Stream stream, ref int totalBytes)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            // Read at most one byte beyond either limit, including streams without Length/Seek.
            var allowance = Math.Min(MaximumModelBytes - (int)bytes.Length, MaximumCatalogBytes - totalBytes);
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, allowance + 1));
            if (read == 0) break;
            totalBytes += read;
            if (totalBytes > MaximumCatalogBytes) throw new InvalidDataException("model catalog exceeds total byte limit");
            if (bytes.Length + read > MaximumModelBytes) throw new InvalidDataException("model exceeds byte limit");
            bytes.Write(buffer, 0, read);
        }
        var json = StrictUtf8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
        if (json.StartsWith('\uFEFF')) json = json[1..];
        if (json.Length > BlockModelCompiler.MaximumJsonCharacters) throw new InvalidDataException("model exceeds JSON character limit");
        return json;
    }
}
