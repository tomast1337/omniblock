using Microsoft.Extensions.Logging;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using Silk.NET.OpenGL;
using AddressMode = Silk.NET.WebGPU.AddressMode;
using FilterMode = Silk.NET.WebGPU.FilterMode;
using MipmapFilterMode = Silk.NET.WebGPU.MipmapFilterMode;

namespace OmniBlock.Client.Rendering.Core.Textures;

/// <summary>
///     A GPU texture array: every layer the same pixel size, one bind covering however many named
///     textures it holds.
/// </summary>
/// <remarks>
///     The API is shaped like OpenGL's and reshapes it the same way <see cref="Texture2D" /> does
///     — an array's size and layer count are fixed at creation, so the real texture is made at the
///     upload that finally states them, and the filtering is carried as data until a sampler can be
///     built from it.
/// </remarks>
public sealed class TextureArray : IDisposable
{
    private static readonly ILogger s_logger = Log.Instance.For<TextureArray>();
    private static readonly Dictionary<uint, (string Source, DateTime CreatedAt)> s_activeTextures = [];
    private static uint s_nextWebGpuId;

    private readonly bool _mipmapped;
    private WgpuSamplerDescription _sampler = WgpuSamplerDescription.Nearest;

    public TextureArray(string source, bool mipmapped = false)
    {
        Source = source;
        _mipmapped = mipmapped;
        Id = ++s_nextWebGpuId;
        s_activeTextures.Add(Id, (source, DateTime.Now));
    }

    public uint Id { get; private set; }
    public string Source { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int LayerCount { get; private set; }
    public int MipLevelCount { get; private set; } = 1;
    public static int ActiveTextureCount => s_activeTextures.Count;

    /// <summary>The WebGPU array, once an upload has given it a size.</summary>
    public WgpuTextureArray? Wgpu { get; private set; }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Id == 0) return;

        Wgpu?.Dispose();
        Wgpu = null;

        s_activeTextures.Remove(Id, out _);
        Id = 0;
    }

    public void Bind()
    {
        if (Id == 0) return;

        // Nothing to bind: an array is sampled through the bind group of whichever pipeline reads
        // it, which the renderer sets from the array itself.
        TextureStats.NotifyBind();
    }

    public void SetFilter(TextureMinFilter min, TextureMagFilter mag)
    {
        UpdateSampler(_sampler with
        {
            Min = min is TextureMinFilter.Linear or TextureMinFilter.LinearMipmapLinear
                or TextureMinFilter.LinearMipmapNearest
                ? FilterMode.Linear
                : FilterMode.Nearest,
            Mag = mag == TextureMagFilter.Linear ? FilterMode.Linear : FilterMode.Nearest,
            Mipmap = min is TextureMinFilter.LinearMipmapLinear or TextureMinFilter.NearestMipmapLinear
                ? MipmapFilterMode.Linear
                : MipmapFilterMode.Nearest
        });
    }

    public void SetWrap(TextureWrapMode s, TextureWrapMode t) => UpdateSampler(_sampler with
    {
        AddressU = ToAddressMode(s),
        AddressV = ToAddressMode(t)
    });

    public void SetMaxLevel(int level) => UpdateSampler(_sampler with
    {
        LodMaxClamp = level
    });

    /// <summary>
    ///     (Re)allocates the whole array at <paramref name="width" />x<paramref name="height" /> with
    ///     <paramref name="layerCount" /> layers, uploading <paramref name="ptr" /> as layer data
    ///     packed contiguously — layer 0's pixels, then layer 1's, and so on.
    /// </summary>
    /// <remarks>
    ///     Reallocates rather than resizing in place, because the only time the size changes is a
    ///     resolution-policy rebuild (see <see cref="Atlas.NamedTextureArray" />), which touches every
    ///     layer at once — there is no cheaper partial path to preserve.
    /// </remarks>
    public unsafe void Upload(int width, int height, int layerCount, byte* ptr, int level = 0, GLEnum format = GLEnum.Rgba, GLEnum internalFormat = GLEnum.Rgba8)
    {
        if (level == 0)
        {
            Width = width;
            Height = height;
            LayerCount = layerCount;
            MipLevelCount = _mipmapped
                ? 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)))
                : 1;
        }

        // Callers provide only the base level. The terrain array derives each layer's filtered
        // levels independently below; item arrays still have one level.
        if (level != 0) return;

        Materialize();
        if (Wgpu is null) return;

        var layerBytes = width * height * 4;
        for (var layer = 0; layer < layerCount; layer++)
        {
            var pixels = new ReadOnlySpan<byte>(ptr + layer * layerBytes, layerBytes);
            if (_mipmapped)
            {
                var mips = TerrainTileMipmaps.Build(pixels, width, height);
                for (var mip = 0; mip < mips.Length; mip++)
                    Wgpu.UploadMipLayer((uint)layer, (uint)mip, mips[mip]);
            }
            else
            {
                Wgpu.UploadLayer((uint)layer, pixels);
            }
        }
    }

    /// <summary>Replaces a single layer's pixels — a texture-pack override, or an animated tile tick.</summary>
    public unsafe void UploadLayer(int layer, int width, int height, byte* ptr, int level = 0, GLEnum format = GLEnum.Rgba)
    {
        if (level != 0 || Wgpu is null) return;
        if (_mipmapped && (width != Width || height != Height))
            throw new ArgumentException(
                "Filtered terrain layers must replace the whole base tile before rebuilding mipmaps.");

        var pixels = new ReadOnlySpan<byte>(ptr, checked(width * height * 4));
        if (_mipmapped)
        {
            var mips = TerrainTileMipmaps.Build(pixels, width, height);
            for (var mip = 0; mip < mips.Length; mip++)
                Wgpu.UploadMipLayer((uint)layer, (uint)mip, mips[mip]);
        }
        else
        {
            Wgpu.UploadLayer((uint)layer, pixels);
        }
    }

    public static void LogLeakReport()
    {
        if (s_activeTextures.Count == 0) return;

        s_logger.LogWarning("Found {Count} leaked texture arrays on shutdown!", s_activeTextures.Count);
        foreach (var entry in s_activeTextures)
        {
            s_logger.LogWarning("Leaked Texture Array ID: {Id}, Source: {Source}, Created At: {CreatedAt}", entry.Key, entry.Value.Source, entry.Value.CreatedAt);
        }
    }

    private void Materialize()
    {
        if (WebGpuDevice.Current is not { } device) return;

        Wgpu?.Dispose();
        Wgpu = new WgpuTextureArray(device, (uint)Width, (uint)Height, (uint)LayerCount,
            _sampler, (uint)MipLevelCount);
    }

    private void UpdateSampler(WgpuSamplerDescription sampler)
    {
        _sampler = sampler;
        Wgpu?.SetSampler(sampler);
    }

    private static AddressMode ToAddressMode(TextureWrapMode mode) => mode switch
    {
        TextureWrapMode.ClampToEdge or TextureWrapMode.ClampToBorder => AddressMode.ClampToEdge,
        TextureWrapMode.MirroredRepeat => AddressMode.MirrorRepeat,
        _ => AddressMode.Repeat
    };
}
