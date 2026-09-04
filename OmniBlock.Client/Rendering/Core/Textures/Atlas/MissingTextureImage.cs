using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OmniBlock.Client.Rendering.Core.Textures.Atlas;

/// <summary>
///     The classic magenta-and-black checkerboard: the absolute fallback for a name that resolves
///     to nothing — not overridden by a pack, not among the sliced defaults either.
/// </summary>
/// <remarks>
///     <see cref="TextureManager" /> already builds one of these for its own unrelated fallback use
///     (a whole missing image, fixed at 256x256); this is the same pattern generalized to whatever
///     size an array's layers currently are, since <see cref="NamedTextureArray" /> can rebuild at a
///     different resolution.
/// </remarks>
public static class MissingTextureImage
{
    public static Image<Rgba32> Generate(int size)
    {
        var image = new Image<Rgba32>(size, size);
        var half = size / 2;

        image.Mutate(ctx =>
        {
            ctx.BackgroundColor(Color.Magenta);
            ctx.Fill(Color.Black, new RectangleF(0, 0, half, half));
            ctx.Fill(Color.Black, new RectangleF(half, half, size - half, size - half));
        });

        return image;
    }
}
