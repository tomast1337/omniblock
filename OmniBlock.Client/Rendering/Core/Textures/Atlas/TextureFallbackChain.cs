using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Client.Rendering.Core.Textures.Atlas;

/// <summary>Which link of the fallback chain answered a <see cref="TextureFallbackChain.Resolve" /> call.</summary>
public enum TextureSource
{
    /// <summary>The active texture pack shipped <c>textures/&lt;domain&gt;/&lt;name&gt;.png</c>.</summary>
    Pack,

    /// <summary>Sliced from the built-in grid atlas — the pack didn't override this name.</summary>
    Default,

    /// <summary>Neither pack nor default had it; the procedural magenta checkerboard.</summary>
    Fallback
}

public readonly record struct ResolvedTexture(TextureSource Source, Image<Rgba32> Image);

/// <summary>
///     Resolves one named texture through Custom Pack → Default → Fallback checkerboard.
/// </summary>
/// <remarks>
///     Deliberately GL-free — takes and returns <see cref="Image{TPixel}" /> so the fallback
///     decision is unit-testable without a live context. <see cref="NamedTextureArray" /> is the
///     GL-touching caller: it resolves every name this way, then packs the results into one array
///     upload.
/// </remarks>
public static class TextureFallbackChain
{
    /// <param name="name">The texture's name, e.g. <c>grass_block_top</c>.</param>
    /// <param name="openPackOverride">
    ///     Opens <c>textures/&lt;domain&gt;/&lt;name&gt;.png</c> from the active pack, or
    ///     <see langword="null" /> if it isn't there. The caller owns closing nothing — this method
    ///     consumes the stream fully before returning.
    /// </param>
    /// <param name="defaults">The sliced built-in atlas, name to tile image.</param>
    /// <param name="fallbackSize">Pixel size to generate the checkerboard at when nothing resolves.</param>
    public static ResolvedTexture Resolve(
        string name,
        Func<string, Stream?> openPackOverride,
        IReadOnlyDictionary<string, Image<Rgba32>> defaults,
        int fallbackSize)
    {
        Stream? packStream = openPackOverride(name);
        if (packStream != null)
        {
            using (packStream)
            {
                try
                {
                    return new ResolvedTexture(TextureSource.Pack, Image.Load<Rgba32>(packStream));
                }
                catch
                {
                    // Corrupt or unreadable pack override — fall through to the default tile rather
                    // than taking the whole array down over one bad file.
                }
            }
        }

        if (defaults.TryGetValue(name, out Image<Rgba32>? def))
        {
            return new ResolvedTexture(TextureSource.Default, def.Clone());
        }

        return new ResolvedTexture(TextureSource.Fallback, MissingTextureImage.Generate(fallbackSize));
    }
}
