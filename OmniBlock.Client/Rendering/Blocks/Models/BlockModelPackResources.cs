using System.IO.Compression;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Strict, independently owned pack reader plus a lazy legacy terrain-atlas bridge.</summary>
internal sealed class BlockModelPackResources : IDisposable
{
    private readonly AtlasTileMap _map;
    private readonly Func<string, Stream?> _override;
    private readonly Func<string, Stream?> _builtin;
    private readonly Func<Image<Rgba32>> _defaultGrid;
    private readonly Dictionary<string, AtlasTile> _tiles;
    private ZipArchive? _archive;
    private Image<Rgba32>? _packGrid;
    private Image<Rgba32>? _builtinGrid;
    private bool _packGridChecked;

    internal BlockModelPackResources(AtlasTileMap map, Func<string, Stream?> openOverride,
        Func<string, Stream?> openBuiltin, Func<Image<Rgba32>> defaultGrid)
    {
        _map = map;
        _override = openOverride;
        _builtin = openBuiltin;
        _defaultGrid = defaultGrid;
        _tiles = map.Tiles.ToDictionary(t => $"assets/omniblock/textures/{t.Name}.png", StringComparer.Ordinal);
    }

    public static BlockModelPackResources Open(TexturePack pack)
    {
        ZipArchive? archive = null;
        try
        {
            if (pack is ZippedTexturePack zipped) archive = ZipFile.OpenRead(zipped.SourceFile.FullName);
            else if (pack is not BuiltInTexturePack) throw new NotSupportedException("Unsupported model resource pack type.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            if (archive != null)
                foreach (var entry in archive.Entries)
                    if (!entries.TryAdd(entry.FullName, entry))
                        throw new InvalidDataException($"Duplicate pack resource '{entry.FullName}'.");
            var source = new BlockModelPackResources(Atlases.Terrain,
                path => entries.TryGetValue(path, out var entry) ? entry.Open() : null,
                path =>
                {
                    // The model/texture loaders generate validated relative asset paths.
                    var fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
                    var assetRoot = Path.Combine(AppContext.BaseDirectory, "assets") + Path.DirectorySeparatorChar;
                    if (!fullPath.StartsWith(assetRoot, StringComparison.Ordinal)) throw new InvalidDataException("Resource path escapes assets.");
                    return File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
                },
                () => Image.Load<Rgba32>(AssetManager.Instance.GetAsset("terrain.png").GetBinaryContent()));
            source._archive = archive;
            return source;
        }
        catch { archive?.Dispose(); throw; }
    }

    public Dictionary<RenderResourceId, int> FixedLayers() => _map.Tiles.ToDictionary(
        tile => RenderResourceId.Parse("omniblock:" + tile.Name), tile => _map.LayerOf(tile.Name));

    public Stream? OpenOverride(string path)
    {
        var direct = _override(path);
        if (direct != null || !_tiles.TryGetValue(path, out var tile)) return direct;
        var named = _override($"textures/terrain/{tile.Name}.png");
        if (named != null) return named;
        if (!_packGridChecked)
        {
            // Failure is not cached as absence; callers abort the candidate and dispose this source.
            using var stream = _override("terrain.png");
            if (stream != null)
            {
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                const int limit = 32 * 1024 * 1024;
                int read;
                while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, limit - (int)bytes.Length + 1))) != 0)
                {
                    bytes.Write(buffer, 0, read);
                    if (bytes.Length > limit) throw new InvalidDataException("Terrain atlas exceeds encoded byte limit.");
                }
                var data = bytes.ToArray();
                var info = Image.Identify(data);
                ValidateGrid(info.Width, info.Height);
                _packGrid = Image.Load<Rgba32>(new SixLabors.ImageSharp.Formats.DecoderOptions { MaxFrames = 1 }, data);
            }
            _packGridChecked = true;
        }
        return _packGrid is null ? null : Tile(_packGrid, tile);
    }

    public Stream? OpenBuiltin(string path)
    {
        var direct = _builtin(path);
        if (direct != null || !_tiles.TryGetValue(path, out var tile)) return direct;
        _builtinGrid ??= _defaultGrid();
        ValidateGrid(_builtinGrid.Width, _builtinGrid.Height);
        return Tile(_builtinGrid, tile);
    }

    private void ValidateGrid(int width, int height)
    {
        if (width <= 0 || height <= 0 || width % _map.GridWidth != 0 || height % _map.GridHeight != 0 ||
            width / _map.GridWidth != height / _map.GridHeight ||
            width / _map.GridWidth > PreparedBlockModelResources.MaximumTextureSize ||
            (long)width * height * 4 > PreparedBlockModelResources.MaximumPixelBytes)
            throw new InvalidDataException("Terrain atlas dimensions exceed limits or do not match the tile grid.");
    }

    private Stream Tile(Image<Rgba32> grid, AtlasTile tile)
    {
        var size = grid.Width / _map.GridWidth;
        using var image = grid.Clone(ctx => ctx.Crop(new Rectangle(tile.X * size, tile.Y * size, size, size)));
        var stream = new MemoryStream();
        try { image.SaveAsPng(stream); stream.Position = 0; return stream; }
        catch { stream.Dispose(); throw; }
    }

    public void Dispose()
    {
        _packGrid?.Dispose();
        _builtinGrid?.Dispose();
        _archive?.Dispose();
    }
}
