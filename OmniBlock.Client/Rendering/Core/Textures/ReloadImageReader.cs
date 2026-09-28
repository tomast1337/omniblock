using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Client.Rendering.Core.Textures;

/// <summary>Bounded strict decoding for one reload. The caller owns each returned image.</summary>
internal sealed class ReloadImageReader(Func<string, Stream?> open)
{
    private const int EncodedLimit = 32 * 1024 * 1024;
    private const long PixelLimit = 256 * 1024 * 1024;
    private long _pixels;

    public Image<Rgba32>? Read(string path)
    {
        try
        {
            using var stream = open(path);
            if (stream is null) return null;
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, EncodedLimit - (int)bytes.Length + 1))) != 0)
            {
                bytes.Write(buffer, 0, read);
                if (bytes.Length > EncodedLimit) throw new InvalidDataException("Encoded image exceeds limit.");
            }
            var data = bytes.ToArray();
            var info = Image.Identify(new DecoderOptions { MaxFrames = 2 }, data);
            var size = (long)info.Width * info.Height * 4;
            if (info.FrameMetadataCollection.Count > 1 || size > PixelLimit - _pixels)
                throw new InvalidDataException("Image exceeds reload pixel limit or is animated.");
            _pixels += size;
            return Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, data);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Texture '{path}': {ex.Message}", ex);
        }
    }

    public static void ValidateGrid(Image<Rgba32> image, string path)
    {
        if (image.Width != image.Height || image.Width % 16 != 0 || image.Width / 16 is < 16 or > 512)
            throw new InvalidDataException($"Texture '{path}': expected a square 16x16 grid with tiles of size 16..512.");
    }

    public static void ValidateColors(Image<Rgba32> image, string path)
    {
        if (image.Width != 256 || image.Height != 256)
            throw new InvalidDataException($"Color map '{path}': expected 256x256 pixels.");
    }
}
