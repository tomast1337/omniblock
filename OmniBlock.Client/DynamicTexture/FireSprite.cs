using OmniBlock.Textures;

namespace OmniBlock.Client.DynamicTexture;

internal class FireSprite(string tile, string customTexture) : Rendering.Core.Textures.DynamicTexture(Atlases.Terrain.IndexOf(tile))
{
    private float[] _current = new float[320];
    private float[] _next = new float[320];

    public override void Setup(OmniBlock game)
    {
        Array.Clear(_current);
        Array.Clear(_next);
        TryLoadCustomTexture(game, customTexture);
    }

    public override void tick()
    {
        if (CustomFrames != null)
        {
            Buffer.BlockCopy(CustomFrames[CustomFrameIndex], 0, Pixels, 0, Pixels.Length);
            CustomFrameIndex = (CustomFrameIndex + 1) % CustomFrameCount;
            return;
        }

        for (var x = 0; x < 16; ++x)
        {
            for (var y = 0; y < 20; ++y)
            {
                var weight = 18;
                var heat = _current[x + (y + 1) % 20 * 16] * weight;

                for (var nx = x - 1; nx <= x + 1; ++nx)
                {
                    for (var ny = y; ny <= y + 1; ++ny)
                    {
                        if (nx >= 0 && ny >= 0 && nx < 16 && ny < 20)
                        {
                            heat += _current[nx + ny * 16];
                        }

                        ++weight;
                    }
                }

                _next[x + y * 16] = heat / (weight * 1.06F);

                if (y >= 19)
                {
                    _next[x + y * 16] = (float)(Random.Shared.NextDouble() * Random.Shared.NextDouble() * Random.Shared.NextDouble() * 4.0D + Random.Shared.NextDouble() * 0.1F + 0.2F);
                }
            }
        }

        (_next, _current) = (_current, _next);

        for (var pixelIndex = 0; pixelIndex < 256; ++pixelIndex)
        {
            var intensity = _current[pixelIndex] * 1.8F;

            if (intensity > 1.0F)
            {
                intensity = 1.0F;
            }

            if (intensity < 0.0F)
            {
                intensity = 0.0F;
            }

            var r = (int)(intensity * 155.0F + 100.0F);
            var g = (int)(intensity * intensity * 255.0F);
            var b = (int)(intensity * intensity * intensity * intensity * intensity * intensity * intensity * intensity * intensity * intensity * 255.0F);
            short a = 255;

            if (intensity < 0.5F)
            {
                a = 0;
            }

            Pixels[pixelIndex * 4 + 0] = (byte)r;
            Pixels[pixelIndex * 4 + 1] = (byte)g;
            Pixels[pixelIndex * 4 + 2] = (byte)b;
            Pixels[pixelIndex * 4 + 3] = (byte)a;
        }
    }
}
