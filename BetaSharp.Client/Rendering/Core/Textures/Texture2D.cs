using BetaSharp.Client.Rendering.Core.WebGPU;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using AddressMode = Silk.NET.WebGPU.AddressMode;
using FilterMode = Silk.NET.WebGPU.FilterMode;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;
using MipmapFilterMode = Silk.NET.WebGPU.MipmapFilterMode;

namespace BetaSharp.Client.Rendering.Core.Textures;

/// <summary>
///     One 2D texture, on whichever backend is running.
/// </summary>
/// <remarks>
///     <para>
///         The API is OpenGL's, because the callers are: allocate a name, upload a level, set the
///         filtering afterwards, upload a sub-rectangle later still. WebGPU allows none of that
///         order — a texture's size and mip count are fixed at creation and its filtering lives in
///         an immutable sampler — so the WebGPU side records what it is told and materializes the
///         real texture at the first upload, when the size is finally known.
///     </para>
///     <para>
///         <see cref="Id" /> is the GL name under OpenGL and a handed-out number under WebGPU.
///         Either way it is what the batching renderers key their buckets on, and
///         <see cref="Find" /> takes it back to the texture at flush.
///     </para>
/// </remarks>
public class Texture2D : IDisposable
{
    private static readonly ILogger s_logger = Log.Instance.For<Texture2D>();
    private static readonly Dictionary<uint, (string Source, DateTime CreatedAt)> s_activeTextures = [];
    private static readonly Dictionary<uint, Texture2D> s_byId = [];
    private static uint s_nextWebGpuId;

    public uint Id { get; private set; }
    public string Source { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public static int ActiveTextureCount => s_activeTextures.Count;

    /// <summary>The WebGPU texture, once an upload has given it a size. Null under OpenGL.</summary>
    public WgpuTexture? Wgpu { get; private set; }

    private WgpuSamplerDescription _sampler = WgpuSamplerDescription.Nearest;

    public Texture2D(string source)
    {
        Source = source;
        Id = GLManager.GLOrNull is { } gl ? gl.GenTexture() : ++s_nextWebGpuId;
        s_activeTextures.Add(Id, (source, DateTime.Now));
        s_byId[Id] = this;
    }

    /// <summary>The texture a renderer's bucket id refers to, or null if it has been disposed.</summary>
    public static Texture2D? Find(uint id) => s_byId.GetValueOrDefault(id);

    /// <summary>The last texture <see cref="Bind" /> was called on. Only tracked under WebGPU.</summary>
    /// <remarks>
    ///     GL keeps this itself, in the 2D binding point of the active texture unit, and a draw
    ///     samples whatever is there. WebGPU has no binding point outside a render pass, so the
    ///     intent a caller expressed by binding has to be remembered until a draw can act on it.
    /// </remarks>
    public static Texture2D? Bound { get; private set; }

    public void Bind()
    {
        if (Id == 0) return;

        TextureStats.NotifyBind();

        if (GLManager.GLOrNull is { } gl)
        {
            gl.BindTexture(GLEnum.Texture2D, Id);
            return;
        }

        Bound = this;
    }

    public void SetFilter(TextureMinFilter min, TextureMagFilter mag)
    {
        if (GLManager.GLOrNull is { } gl)
        {
            Bind();
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)min);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)mag);
            return;
        }

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
        if (GLManager.GLOrNull is { } gl)
        {
            Bind();
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)s);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)t);
            return;
        }

        UpdateSampler(_sampler with { AddressU = ToAddressMode(s), AddressV = ToAddressMode(t) });
    }

    public void SetMaxLevel(int level)
    {
        if (GLManager.GLOrNull is { } gl)
        {
            Bind();
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMaxLevel, level);
            return;
        }

        UpdateSampler(_sampler with { LodMaxClamp = level });
    }

    public unsafe void Upload(int width, int height, byte* ptr, int level = 0, PixelFormat format = PixelFormat.Rgba, InternalFormat internalFormat = InternalFormat.Rgba)
    {
        if (level == 0)
        {
            Width = width;
            Height = height;
        }

        if (GLManager.GLOrNull is { } gl)
        {
            Bind();
            gl.TexImage2D(TextureTarget.Texture2D, level, internalFormat, (uint)width, (uint)height, 0, format, PixelType.UnsignedByte, ptr);
            return;
        }

        // Level 0 is the one that fixes the size, so it is also what creates the texture. Levels
        // arrive afterwards, smallest last, and write into the chain allocated here.
        if (level == 0) Materialize();
        WriteWgpu(level, 0, 0, width, height, ptr);
    }

    public unsafe void UploadSubImage(int x, int y, int width, int height, byte* ptr, int level = 0, PixelFormat format = PixelFormat.Rgba)
    {
        if (GLManager.GLOrNull is { } gl)
        {
            Bind();
            gl.TexSubImage2D(GLEnum.Texture2D, level, x, y, (uint)width, (uint)height, (GLEnum)format, (GLEnum)PixelType.UnsignedByte, ptr);
            return;
        }

        WriteWgpu(level, x, y, width, height, ptr);
    }

    public void SetAnisotropicFilter(float level)
    {
        if (GLManager.GLOrNull is { } gl)
        {
            if (!gl.IsExtensionPresent("GL_EXT_texture_filter_anisotropic")) return;

            Bind();
            gl.TexParameter(GLEnum.Texture2D, (GLEnum)0x84FE, level); // GL_TEXTURE_MAX_ANISOTROPY_EXT
            return;
        }

        UpdateSampler(_sampler with { MaxAnisotropy = (uint)Math.Max(1.0f, level) });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Id == 0) return;

        Wgpu?.Dispose();
        Wgpu = null;

        if (ReferenceEquals(Bound, this)) Bound = null;

        GLManager.GLOrNull?.DeleteTexture(Id);
        s_activeTextures.Remove(Id, out _);
        s_byId.Remove(Id);
        Id = 0;
    }

    public static void LogLeakReport()
    {
        if (s_activeTextures.Count == 0) return;

        s_logger.LogWarning("Found {Count} leaked textures on shutdown!", s_activeTextures.Count);
        foreach (KeyValuePair<uint, (string Source, DateTime CreatedAt)> entry in s_activeTextures)
        {
            s_logger.LogWarning("Leaked Texture ID: {Id}, Source: {Source}, Created At: {CreatedAt}", entry.Key, entry.Value.Source, entry.Value.CreatedAt);
        }
    }

    /// <summary>
    ///     Creates the WebGPU texture with a full mip chain, whether or not every level is later
    ///     written. An unwritten level costs a quarter of the one above it and is never sampled,
    ///     because <see cref="SetMaxLevel" /> clamps the sampler to what was uploaded.
    /// </summary>
    private void Materialize()
    {
        if (WebGpuDevice.Current is not { } device) return;

        Wgpu?.Dispose();

        uint levels = (uint)Math.Max(1, (int)Math.Log2(Math.Max(Width, Height)) + 1);
        Wgpu = new WgpuTexture(device, (uint)Width, (uint)Height, levels, _sampler);
    }

    private unsafe void WriteWgpu(int level, int x, int y, int width, int height, byte* ptr)
    {
        if (Wgpu is null || width <= 0 || height <= 0) return;

        Wgpu.WriteLevel((uint)level, (uint)x, (uint)y, (uint)width, (uint)height,
            new ReadOnlySpan<byte>(ptr, width * height * 4));
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
