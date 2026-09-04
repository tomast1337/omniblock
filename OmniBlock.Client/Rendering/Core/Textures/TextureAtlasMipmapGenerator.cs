using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OmniBlock.Client.Rendering.Core.Textures;

public static class TextureAtlasMipmapGenerator
{
    public static Image<Rgba32>[] GenerateMipmaps(Image<Rgba32> atlas, int tileSize)
    {
        var maxMipLevels = (int)Math.Log2(tileSize) + 1;
        var mipLevels = new Image<Rgba32>[maxMipLevels];

        mipLevels[0] = atlas.Clone();

        for (var mipLevel = 1; mipLevel < maxMipLevels; mipLevel++)
        {
            var scale = 1 << mipLevel;
            var newWidth = atlas.Width / scale;
            var newHeight = atlas.Height / scale;
            mipLevels[mipLevel] = atlas.Clone(ctx => ctx.Resize(newWidth, newHeight, KnownResamplers.Box));
        }

        return mipLevels;
    }

    public static byte[] ToByteArray(Image<Rgba32> image)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(bytes);
        return bytes;
    }
}
