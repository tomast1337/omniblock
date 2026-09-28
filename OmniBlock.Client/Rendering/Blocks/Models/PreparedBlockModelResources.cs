using System.Collections.Frozen;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using OmniBlock.Client.Rendering.Core.WebGPU;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Immutable CPU candidate with models and their exact texture bindings/pixels. Preparation never
/// reads a live atlas or changes the active pack. Upload creates a new, independently owned array.
/// </summary>
internal sealed class PreparedBlockModelResources
{
    public const int MaximumTextureSize = 512;
    public const int MaximumTextureBytes = 4 * 1024 * 1024;
    public const int MaximumEncodedBytes = 64 * 1024 * 1024;
    public const int MaximumPixelBytes = 128 * 1024 * 1024;
    private readonly byte[] _pixels;

    private PreparedBlockModelResources(BlockModelCatalog models, Dictionary<RenderResourceId, int> layers,
        Dictionary<RenderResourceId, TextureSource> sources, int size, int layerCount, byte[] pixels)
    {
        Models = models;
        Layers = layers.ToFrozenDictionary();
        Sources = sources.ToFrozenDictionary();
        LayerSize = size;
        LayerCount = layerCount;
        _pixels = pixels;
    }

    public BlockModelCatalog Models { get; }
    public FrozenDictionary<RenderResourceId, int> Layers { get; }
    public FrozenDictionary<RenderResourceId, TextureSource> Sources { get; }
    public int LayerSize { get; }
    public int LayerCount { get; }
    public ReadOnlySpan<byte> Pixels => _pixels;

    public static PreparedBlockModelResources Build(IEnumerable<RenderResourceId> entryPoints,
        IReadOnlyDictionary<RenderResourceId, int> fixedLayers,
        Func<string, Stream?> openOverride, Func<string, Stream?> openBuiltin)
    {
        ArgumentNullException.ThrowIfNull(fixedLayers);
        // Capture bindings before invoking external resource readers. Existing detailed meshes
        // must retain their layer meanings; new names take the first free slot in ordinal order.
        var layers = new Dictionary<RenderResourceId, int>(fixedLayers);
        var occupied = new HashSet<int> { 0 };
        foreach (var (id, layer) in layers)
            if (layer is < 1 or > 255 || !occupied.Add(layer))
                throw new InvalidDataException($"Texture '{id}': invalid or duplicate fixed layer {layer}.");

        var requested = new HashSet<RenderResourceId>(layers.Keys);
        var unbound = BlockModelPackLoader.Build(entryPoints, openOverride, openBuiltin, id =>
        {
            requested.Add(id);
            if (requested.Count > 255) throw new InvalidDataException("Candidate exceeds the 255 usable texture layers.");
            return 1; // Private symbolic compilation only; never published or uploaded.
        });
        foreach (var id in requested.OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            if (layers.ContainsKey(id)) continue;
            var layer = Enumerable.Range(1, 255).FirstOrDefault(i => !occupied.Contains(i));
            if (layer == 0) throw new InvalidDataException("Candidate exceeds the 255 usable texture layers.");
            layers.Add(id, layer);
            occupied.Add(layer);
        }

        var images = new Dictionary<RenderResourceId, Image<Rgba32>>();
        var sources = new Dictionary<RenderResourceId, TextureSource>();
        var encodedBytes = 0;
        long decodedBytes = 0;
        var size = 16;
        try
        {
            foreach (var id in layers.Keys.OrderBy(id => id.ToString(), StringComparer.Ordinal))
            {
                var path = $"assets/{id.Namespace}/textures/{id.Path}.png";
                var source = TextureSource.Pack;
                try
                {
                    var stream = openOverride(path);
                    if (stream is null)
                    {
                        source = TextureSource.Default;
                        stream = openBuiltin(path);
                    }
                    if (stream is null) throw new InvalidDataException("required texture is missing");
                    byte[] encoded;
                    using (stream) encoded = ReadTexture(stream, ref encodedBytes);
                    if (Image.DetectFormat(encoded).Name != "PNG") throw new InvalidDataException("expected a PNG texture");
                    var info = Image.Identify(new SixLabors.ImageSharp.Formats.DecoderOptions { MaxFrames = 2 }, encoded);
                    if (info.Width != info.Height || info.Width is < 1 or > MaximumTextureSize)
                        throw new InvalidDataException($"expected a square texture of at most {MaximumTextureSize} pixels");
                    if (info.FrameMetadataCollection.Count > 1)
                        throw new InvalidDataException("animated model PNGs are not supported; use the animation provider");
                    decodedBytes += (long)info.Width * info.Height * 4;
                    if (decodedBytes > MaximumPixelBytes) throw new InvalidDataException("decoded texture budget exceeded");
                    // Only the first frame is decoded: animated terrain still uses its existing
                    // procedural provider. Model resources currently describe static PNG tiles.
                    var image = Image.Load<Rgba32>(new SixLabors.ImageSharp.Formats.DecoderOptions { MaxFrames = 1 }, encoded);
                    images.Add(id, image);
                    sources.Add(id, source);
                    size = Math.Max(size, image.Width);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException or UnauthorizedAccessException)
                {
                    throw new InvalidDataException($"Texture '{id}' ({source} '{path}'): {ex.Message}", ex);
                }
            }

            var count = occupied.Max() + 1;
            var byteCount = (long)size * size * 4 * count;
            if (byteCount > MaximumPixelBytes) throw new InvalidDataException("Packed texture array exceeds pixel budget.");
            var pixels = new byte[(int)byteCount];
            var stride = size * size * 4;
            using var missing = MissingTextureImage.Generate(size);
            // Reserved layer and unused fixed-layer gaps remain visibly missing, not transparent.
            for (var i = 0; i < count; i++) missing.CopyPixelDataTo(pixels.AsSpan(i * stride, stride));
            foreach (var (id, image) in images)
            {
                using var resized = image.Clone(ctx => ctx.Resize(size, size, KnownResamplers.NearestNeighbor));
                resized.CopyPixelDataTo(pixels.AsSpan(layers[id] * stride, stride));
            }
            return new PreparedBlockModelResources(unbound.BindTextures(layers), layers, sources, size, count, pixels);
        }
        finally
        {
            foreach (var image in images.Values) image.Dispose();
        }
    }

    /// <summary>
    /// Render-thread preparation only, before frame encoding. Does not publish anything. The owner
    /// must swap the returned model/texture pair together and retire the old pair after submission.
    /// </summary>
    public unsafe UploadedBlockModelResources Upload(bool mipmapsEnabled)
    {
        var device = WebGpuDevice.Current ?? throw new InvalidOperationException("Model resource upload requires an active WebGPU device.");
        var errorCount = device.ErrorCount;
        var texture = new TextureArray("BlockModelResourceCandidate", mipmapped: true);
        try
        {
            fixed (byte* pixels = _pixels) texture.Upload(LayerSize, LayerSize, LayerCount, pixels);
            texture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            texture.SetMaxLevel(mipmapsEnabled ? texture.MipLevelCount - 1 : 0);
            texture.SetWrap(TextureWrapMode.Repeat, TextureWrapMode.Repeat);
            // Reload is a cold path. Wait for preparation errors before handing back a candidate;
            // even an unrelated device error conservatively aborts instead of replacing live data.
            device.Poll();
            if (texture.Wgpu is null || device.ErrorCount != errorCount)
                throw new InvalidOperationException("WebGPU rejected the model texture candidate.");
            return new UploadedBlockModelResources(Models, texture);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static byte[] ReadTexture(Stream stream, ref int total)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var allowed = Math.Min(MaximumTextureBytes - (int)bytes.Length, MaximumEncodedBytes - total);
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, allowed + 1));
            if (read == 0) return bytes.ToArray();
            total += read;
            if (total > MaximumEncodedBytes || bytes.Length + read > MaximumTextureBytes)
                throw new InvalidDataException("encoded texture byte limit exceeded");
            bytes.Write(buffer, 0, read);
        }
    }
}

/// <summary>One owned GPU candidate. Aborting preparation disposes this, never the live array.</summary>
internal sealed class UploadedBlockModelResources(BlockModelCatalog models, TextureArray texture) : IDisposable
{
    public BlockModelCatalog Models { get; } = models;
    public TextureArray Texture { get; } = texture;
    public void Dispose() => Texture.Dispose();
}
