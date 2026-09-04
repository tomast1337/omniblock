using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OmniBlock.Client.Rendering.Core.Textures;

public class DynamicTexture(int iconIdx)
{
    public enum FxImage
    {
        Terrain,
        Items
    }

    public readonly int Sprite = iconIdx;
    public FxImage Atlas = FxImage.Terrain;
    protected int CustomFrameCount;
    protected int CustomFrameIndex;

    protected byte[][]? CustomFrames;
    public byte[] Pixels = new byte[1024];
    public int Replicate = 1;

    public virtual void Setup(OmniBlock game)
    {
    }

    public virtual void tick()
    {
    }

    protected virtual void TryLoadCustomTexture(OmniBlock game, string resourceName)
    {
        CustomFrames = null;
        CustomFrameIndex = 0;
        CustomFrameCount = 0;

        using var stream = game.TexturePackList.SelectedTexturePack.GetResourceAsStream(resourceName);
        if (stream == null)
        {
            if (Pixels.Length != 1024) Pixels = new byte[1024];
            return;
        }

        try
        {
            var atlasPath = Atlas == FxImage.Terrain ? "/terrain.png" : "/gui/items.png";
            var targetWidth = game.TextureManager.GetTextureId(atlasPath).Texture?.Width ?? 256;
            var targetTileSize = targetWidth / 16;

            if (targetTileSize < 1) targetTileSize = 1;

            using var image = Image.Load<Rgba32>(stream);
            var width = image.Width;
            var height = image.Height;

            if (height % width != 0) return;

            CustomFrameCount = height / width;

            if (width != targetTileSize)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(targetTileSize, targetTileSize * CustomFrameCount),
                    Sampler = KnownResamplers.NearestNeighbor
                }));
                width = image.Width;
                height = image.Height;
            }

            CustomFrames = new byte[CustomFrameCount][];

            var pixelsPerFrame = width * height;
            var bytesPerFrame = pixelsPerFrame * 4;

            if (Pixels.Length != bytesPerFrame)
            {
                Pixels = new byte[bytesPerFrame];
            }

            for (var i = 0; i < CustomFrameCount; i++)
            {
                CustomFrames[i] = new byte[bytesPerFrame];
                var currentFrameIndex = i;

                using var frame = image.Clone(ctx => ctx.Crop(new Rectangle(0, currentFrameIndex * width, width, width)));
                frame.CopyPixelDataTo(CustomFrames[i]);
            }
        }
        catch (Exception)
        {
            CustomFrames = null;
            if (Pixels.Length != 1024) Pixels = new byte[1024];
        }
    }
}
