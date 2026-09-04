using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.DynamicTexture;

internal class NetherPortalSprite() : Rendering.Core.Textures.DynamicTexture(BlockRegistry.Get("nether_portal").TextureId)
{
    private readonly byte[][] _frames = new byte[32][];
    private int _ticks;

    public override void Setup(OmniBlock game)
    {
        TryLoadCustomTexture(game, "custom_portal.png");
        if (CustomFrames != null)
        {
            return;
        }

        JavaRandom random = new(100L);
        for (var i = 0; i < _frames.Length; i++)
        {
            _frames[i] = new byte[1024];
        }

        for (var frameIdx = 0; frameIdx < 32; ++frameIdx)
        {
            for (var x = 0; x < 16; ++x)
            {
                for (var y = 0; y < 16; ++y)
                {
                    var intensity = 0.0F;

                    for (var layer = 0; layer < 2; ++layer)
                    {
                        float offsetX = layer * 8;
                        float offsetY = layer * 8;
                        var distX = (x - offsetX) / 16.0F * 2.0F;
                        var distY = (y - offsetY) / 16.0F * 2.0F;

                        if (distX < -1.0F)
                        {
                            distX += 2.0F;
                        }

                        if (distX >= 1.0F)
                        {
                            distX -= 2.0F;
                        }

                        if (distY < -1.0F)
                        {
                            distY += 2.0F;
                        }

                        if (distY >= 1.0F)
                        {
                            distY -= 2.0F;
                        }

                        var sqDist = distX * distX + distY * distY;
                        var swirlPhase = (float)Math.Atan2(distY, distX) + (frameIdx / 32.0F * (float)Math.PI * 2.0F - sqDist * 10.0F + layer * 2) * (layer * 2 - 1);
                        swirlPhase = (MathHelper.Sin(swirlPhase) + 1.0F) / 2.0F;
                        swirlPhase /= sqDist + 1.0F;
                        intensity += swirlPhase * 0.5F;
                    }

                    intensity += random.NextFloat() * 0.1F;

                    var r = (int)(intensity * intensity * 200.0F + 55.0F);
                    var g = (int)(intensity * intensity * intensity * intensity * 255.0F);
                    var b = (int)(intensity * 100.0F + 155.0F);
                    var a = (int)(intensity * 100.0F + 155.0F);

                    var pixelIdx = y * 16 + x;
                    _frames[frameIdx][pixelIdx * 4 + 0] = (byte)r;
                    _frames[frameIdx][pixelIdx * 4 + 1] = (byte)g;
                    _frames[frameIdx][pixelIdx * 4 + 2] = (byte)b;
                    _frames[frameIdx][pixelIdx * 4 + 3] = (byte)a;
                }
            }
        }
    }

    public override void tick()
    {
        if (CustomFrames != null)
        {
            Buffer.BlockCopy(CustomFrames[CustomFrameIndex], 0, Pixels, 0, Pixels.Length);
            CustomFrameIndex = (CustomFrameIndex + 1) % CustomFrameCount;
            return;
        }

        ++_ticks;
        var currentFrame = _frames[_ticks & 31];

        for (var i = 0; i < 256; ++i)
        {
            var r = currentFrame[i * 4 + 0] & 255;
            var g = currentFrame[i * 4 + 1] & 255;
            var b = currentFrame[i * 4 + 2] & 255;
            var a = currentFrame[i * 4 + 3] & 255;
            Pixels[i * 4 + 0] = (byte)r;
            Pixels[i * 4 + 1] = (byte)g;
            Pixels[i * 4 + 2] = (byte)b;
            Pixels[i * 4 + 3] = (byte)a;
        }
    }
}
