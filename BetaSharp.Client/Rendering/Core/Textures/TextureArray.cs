using BetaSharp.Client.Rendering.Core.WebGPU;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using AddressMode = Silk.NET.WebGPU.AddressMode;
using FilterMode = Silk.NET.WebGPU.FilterMode;
using MipmapFilterMode = Silk.NET.WebGPU.MipmapFilterMode;

namespace BetaSharp.Client.Rendering.Core.Textures;

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

    public uint Id { get; private set; }
    public string Source { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int LayerCount { get; private set; }
    public static int ActiveTextureCount => s_activeTextures.Count;

    /// <summary>The WebGPU array, once an upload has given it a size.</summary>
    public WgpuTextureArray? Wgpu { get; private set; }

    private WgpuSamplerDescription _sampler = WgpuSamplerDescription.Nearest;

    public TextureArray(string source)
    {
        Source = source;
        Id = ++s_nextWebGpuId;
        s_activeTextures.Add(Id, (source, DateTime.Now));
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
                : MipmapFilterMode.Nearest,
        });
    }

    public void SetWrap(TextureWrapMode s, TextureWrapMode t)
    {
        UpdateSampler(_sampler with { AddressU = ToAddressMode(s), AddressV = ToAddressMode(t) });
    }

    public void SetMaxLevel(int level)
    {
        UpdateSampler(_sampler with { LodMaxClamp = level });
    }

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
        }

        // Only the base level exists on this path — nothing builds mips for an array, and a level
        // arriving here would have nowhere to go in a one-level texture.
        if (level != 0) return;

        Materialize();
        if (Wgpu is null) return;

        int layerBytes = width * height * 4;
        for (int layer = 0; layer < layerCount; layer++)
        {
            Wgpu.UploadLayer((uint)layer, new ReadOnlySpan<byte>(ptr + (layer * layerBytes), layerBytes));
        }
    }

    /// <summary>Replaces a single layer's pixels — a texture-pack override, or an animated tile tick.</summary>
    public unsafe void UploadLayer(int layer, int width, int height, byte* ptr, int level = 0, GLEnum format = GLEnum.Rgba)
    {
        if (level != 0 || Wgpu is null) return;

        Wgpu.UploadRegion(0, 0, (uint)layer, (uint)width, (uint)height,
            new ReadOnlySpan<byte>(ptr, width * height * 4));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Id == 0) return;

        Wgpu?.Dispose();
        Wgpu = null;

        s_activeTextures.Remove(Id, out _);
        Id = 0;
    }

    public static void LogLeakReport()
    {
        if (s_activeTextures.Count == 0) return;

        s_logger.LogWarning("Found {Count} leaked texture arrays on shutdown!", s_activeTextures.Count);
        foreach (KeyValuePair<uint, (string Source, DateTime CreatedAt)> entry in s_activeTextures)
        {
            s_logger.LogWarning("Leaked Texture Array ID: {Id}, Source: {Source}, Created At: {CreatedAt}", entry.Key, entry.Value.Source, entry.Value.CreatedAt);
        }
    }

    private void Materialize()
    {
        if (WebGpuDevice.Current is not { } device) return;

        Wgpu?.Dispose();
        Wgpu = new WgpuTextureArray(device, (uint)Width, (uint)Height, (uint)LayerCount, _sampler);
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
        _ => AddressMode.Repeat,
    };
}
