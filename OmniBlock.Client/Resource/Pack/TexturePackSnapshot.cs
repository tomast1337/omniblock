using System.IO.Compression;

namespace OmniBlock.Client.Resource.Pack;

/// <summary>Independently opened candidate resources. Null means absent, never a failed read.</summary>
internal sealed class TexturePackSnapshot : IDisposable
{
    private readonly TexturePack _pack;
    private readonly ZipArchive? _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);

    public TexturePackSnapshot(TexturePack pack)
    {
        _pack = pack;
        if (pack is not ZippedTexturePack zipped) return;
        _archive = ZipFile.OpenRead(zipped.SourceFile.FullName);
        try
        {
            foreach (var entry in _archive.Entries)
                if (!_entries.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException($"Duplicate pack resource '{entry.FullName}'.");
        }
        catch { _archive.Dispose(); throw; }
    }

    public Stream? OpenOverride(string path)
    {
        path = Normalize(path);
        return _archive is null ? _pack.OpenReloadOverride(path)
            : _entries.TryGetValue(path, out var entry) ? entry.Open() : null;
    }

    public static Stream? OpenBuiltin(string path)
    {
        path = Normalize(path);
        if (path.StartsWith("assets/", StringComparison.Ordinal))
        {
            var file = Path.Combine(AppContext.BaseDirectory, path);
            return File.Exists(file) ? File.OpenRead(file) : null;
        }
        return AssetManager.Instance.TryGetAsset(path, out var asset)
            ? new MemoryStream(asset!.GetBinaryContent(), false) : null;
    }

    public Stream? Open(string path) => OpenOverride(path) ?? OpenBuiltin(path);

    private static string Normalize(string path)
    {
        path = path.TrimStart('/');
        if (path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException($"Invalid pack resource path '{path}'.");
        return path;
    }

    public void Dispose() => _archive?.Dispose();
}
