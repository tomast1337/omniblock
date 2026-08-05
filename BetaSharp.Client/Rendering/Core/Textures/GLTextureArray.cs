using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core.Textures;

/// <summary>
///     A GPU texture array: every layer the same pixel size, one bind covering however many named
///     textures it holds.
/// </summary>
/// <remarks>
///     The first real use of <see cref="GLEnum.Texture2DArray" /> in the client — <see cref="GLTexture" />
///     hardcodes <see cref="GLEnum.Texture2D" /> throughout, so it isn't reused here rather than
///     parameterized, matching how little the two share once the target differs.
/// </remarks>
public sealed class GLTextureArray : IDisposable
{
    private static readonly ILogger s_logger = Log.Instance.For<GLTextureArray>();
    private static readonly Dictionary<uint, (string Source, DateTime CreatedAt)> s_activeTextures = [];

    public uint Id { get; private set; }
    public string Source { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int LayerCount { get; private set; }
    public static int ActiveTextureCount => s_activeTextures.Count;

    public GLTextureArray(string source)
    {
        Source = source;
        Id = GLManager.GL.GenTexture();
        s_activeTextures.Add(Id, (source, DateTime.Now));
    }

    public void Bind()
    {
        if (Id != 0)
        {
            TextureStats.NotifyBind();
            GLManager.GL.BindTexture(GLEnum.Texture2DArray, Id);
        }
    }

    public void SetFilter(TextureMinFilter min, TextureMagFilter mag)
    {
        Bind();
        GLManager.GL.TexParameter(GLEnum.Texture2DArray, GLEnum.TextureMinFilter, (int)min);
        GLManager.GL.TexParameter(GLEnum.Texture2DArray, GLEnum.TextureMagFilter, (int)mag);
    }

    public void SetWrap(TextureWrapMode s, TextureWrapMode t)
    {
        Bind();
        GLManager.GL.TexParameter(GLEnum.Texture2DArray, GLEnum.TextureWrapS, (int)s);
        GLManager.GL.TexParameter(GLEnum.Texture2DArray, GLEnum.TextureWrapT, (int)t);
    }

    public void SetMaxLevel(int level)
    {
        Bind();
        GLManager.GL.TexParameter(GLEnum.Texture2DArray, GLEnum.TextureMaxLevel, level);
    }

    /// <summary>
    ///     (Re)allocates the whole array at <paramref name="width" />x<paramref name="height" /> with
    ///     <paramref name="layerCount" /> layers, uploading <paramref name="ptr" /> as layer data
    ///     packed contiguously — layer 0's pixels, then layer 1's, and so on.
    /// </summary>
    /// <remarks>
    ///     Reallocates rather than resizing in place, because the only time the size changes is a
    ///     resolution-policy rebuild (see <see cref="NamedTextureArray" />), which touches every layer
    ///     at once — there is no cheaper partial path to preserve.
    /// </remarks>
    public unsafe void Upload(int width, int height, int layerCount, byte* ptr, int level = 0, GLEnum format = GLEnum.Rgba, GLEnum internalFormat = GLEnum.Rgba8)
    {
        if (level == 0)
        {
            Width = width;
            Height = height;
            LayerCount = layerCount;
        }
        Bind();
        GLManager.GL.TexImage3D(GLEnum.Texture2DArray, level, (int)internalFormat, (uint)width, (uint)height, (uint)layerCount, 0, format, GLEnum.UnsignedByte, ptr);
    }

    /// <summary>Replaces a single layer's pixels — a texture-pack override, or an animated tile tick.</summary>
    public unsafe void UploadLayer(int layer, int width, int height, byte* ptr, int level = 0, GLEnum format = GLEnum.Rgba)
    {
        Bind();
        GLManager.GL.TexSubImage3D(GLEnum.Texture2DArray, level, 0, 0, layer, (uint)width, (uint)height, 1, format, GLEnum.UnsignedByte, ptr);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Id != 0)
        {
            GLManager.GL.DeleteTexture(Id);
            s_activeTextures.Remove(Id, out _);
            Id = 0;
        }
    }

    public static void LogLeakReport()
    {
        if (s_activeTextures.Count == 0) return;

        s_logger.LogWarning("Found {Count} leaked OpenGL texture arrays on shutdown!", s_activeTextures.Count);
        foreach (KeyValuePair<uint, (string Source, DateTime CreatedAt)> entry in s_activeTextures)
        {
            s_logger.LogWarning("Leaked Texture Array ID: {Id}, Source: {Source}, Created At: {CreatedAt}", entry.Key, entry.Value.Source, entry.Value.CreatedAt);
        }
    }
}
