using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OmniBlock.Client.Rendering.Core.Textures;

/// <summary>CPU-only inputs for one candidate pack. Owns decoded images, never live GPU handles.</summary>
internal sealed class TextureAnimationContext(Func<string, Stream?> openResource) : IDisposable
{
    internal const int MaximumFrames = 256;
    internal const int MaximumTileSize = 512;
    private const int MaximumEncodedBytes = 32 * 1024 * 1024;
    private const long MaximumPixelBytes = 128 * 1024 * 1024;
    private readonly Dictionary<string, Image<Rgba32>?> _images = new(StringComparer.Ordinal);
    private long _pixelBytes;
    private bool _disposed;

    private Image<Rgba32>? Load(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_images.TryGetValue(path, out var cached)) return cached;
        try
        {
            using var stream = openResource(path);
            if (stream is null) { _images.Add(path, null); return null; }
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, MaximumEncodedBytes - (int)bytes.Length + 1))) != 0)
            {
                bytes.Write(buffer, 0, read);
                if (bytes.Length > MaximumEncodedBytes) throw new InvalidDataException("Encoded image exceeds limit.");
            }
            var data = bytes.ToArray();
            var info = Image.Identify(new DecoderOptions { MaxFrames = 2 }, data);
            var pixels = (long)info.Width * info.Height * 4;
            if (info.FrameMetadataCollection.Count > 1 || pixels > MaximumPixelBytes - _pixelBytes)
                throw new InvalidDataException("Image exceeds decoded pixel limit or contains multiple image frames.");
            var image = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, data);
            _images.Add(path, image);
            _pixelBytes += pixels;
            return image;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Animation resource '{path}': {ex.Message}", ex);
        }
    }

    private Image<Rgba32> Atlas(DynamicTexture.FxImage atlas)
    {
        var path = atlas == DynamicTexture.FxImage.Terrain ? "terrain.png" : "gui/items.png";
        var image = Load(path) ?? throw new InvalidDataException($"Missing animation atlas '{path}'.");
        if (image.Width != image.Height || image.Width % 16 != 0 || image.Width / 16 is < 2 or > MaximumTileSize)
            throw new InvalidDataException($"Animation atlas '{path}' must be a square 16x16 tile grid, with tile size 2..{MaximumTileSize}.");
        return image;
    }

    internal byte[][]? ReadStrip(string path, DynamicTexture.FxImage atlas)
    {
        var source = Load(path);
        if (source is null) return null;
        if (source.Width > MaximumTileSize || source.Height % source.Width != 0 || source.Height / source.Width > MaximumFrames)
            throw new InvalidDataException($"Animation resource '{path}' must be a vertical strip of at most {MaximumFrames} square frames, width <= {MaximumTileSize}.");
        var count = source.Height / source.Width;
        var size = Atlas(atlas).Width / 16;
        var frameBytes = checked(size * size * 4);
        // Resized pixels and retained per-frame arrays both count against the candidate budget.
        var allocation = (long)frameBytes * count * 2;
        if (allocation > MaximumPixelBytes - _pixelBytes)
            throw new InvalidDataException($"Animation resource '{path}' exceeds prepared pixel limit.");
        _pixelBytes += allocation;
        using var resized = source.Clone(ctx => ctx.Resize(size, size * count, KnownResamplers.NearestNeighbor));
        var frames = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            frames[i] = new byte[frameBytes];
            using var frame = resized.Clone(ctx => ctx.Crop(new Rectangle(0, i * size, size, size)));
            frame.CopyPixelDataTo(frames[i]);
        }
        return frames;
    }

    internal (int Size, int[] Pixels) ReadItemTile(int sprite)
    {
        if (sprite is < 0 or >= 256) throw new ArgumentOutOfRangeException(nameof(sprite));
        var image = Atlas(DynamicTexture.FxImage.Items);
        var size = image.Width / 16;
        return (size, CopyPixels(image, sprite % 16 * size, sprite / 16 * size, size));
    }

    internal (int Size, int[] Pixels) ReadClockDial()
    {
        const string path = "misc/dial.png";
        var image = Load(path) ?? throw new InvalidDataException($"Missing animation resource '{path}'.");
        var size = image.Width;
        // Clock sampling wraps using a bit mask.
        if (size != image.Height || size is < 2 or > MaximumTileSize || (size & (size - 1)) != 0)
            throw new InvalidDataException($"Animation resource '{path}' must be square with power-of-two size 2..{MaximumTileSize}.");
        return (size, CopyPixels(image, 0, 0, size));
    }

    private static int[] CopyPixels(Image<Rgba32> image, int x, int y, int size)
    {
        var result = new int[size * size];
        for (var row = 0; row < size; row++)
        for (var col = 0; col < size; col++)
        {
            var p = image[x + col, y + row];
            result[row * size + col] = (p.A << 24) | (p.R << 16) | (p.G << 8) | p.B;
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var image in _images.Values) image?.Dispose();
        _images.Clear();
    }
}
