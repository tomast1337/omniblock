using OmniBlock.Blocks;

namespace OmniBlock.Client.DynamicTexture;

internal class WaterSprite(IBlockRuntimeView blocks) : Rendering.Core.Textures.DynamicTexture(blocks.Get(ResourceLocation.Parse("omniblock:flowing_water")).TextureId)
{
    private readonly float[] _heat = new float[256];
    private readonly float[] _heatDelta = new float[256];
    private float[] _current = new float[256];
    private float[] _next = new float[256];

    public override void Setup(OmniBlock game) => TryLoadCustomTexture(game, "custom_water_still.png");

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
            for (var y = 0; y < 16; ++y)
            {
                var accumulatedFlow = 0.0F;

                for (var nx = x - 1; nx <= x + 1; ++nx)
                {
                    var sampleX = nx & 15;
                    var sampleY = y & 15;
                    accumulatedFlow += _current[sampleX + sampleY * 16];
                }

                _next[x + y * 16] = accumulatedFlow / 3.3F + _heat[x + y * 16] * 0.8F;
            }
        }

        for (var x = 0; x < 16; ++x)
        {
            for (var y = 0; y < 16; ++y)
            {
                _heat[x + y * 16] += _heatDelta[x + y * 16] * 0.05F;

                if (_heat[x + y * 16] < 0.0F)
                {
                    _heat[x + y * 16] = 0.0F;
                }

                _heatDelta[x + y * 16] -= 0.1F;

                if (Random.Shared.NextDouble() < 0.05D)
                {
                    _heatDelta[x + y * 16] = 0.5F;
                }
            }
        }

        (_next, _current) = (_current, _next);

        for (var pixelIndex = 0; pixelIndex < 256; ++pixelIndex)
        {
            var intensity = _current[pixelIndex];

            if (intensity > 1.0F)
            {
                intensity = 1.0F;
            }

            if (intensity < 0.0F)
            {
                intensity = 0.0F;
            }

            var intensitySq = intensity * intensity;
            var r = (int)(32.0F + intensitySq * 32.0F);
            var g = (int)(50.0F + intensitySq * 64.0F);
            var b = 255;
            var a = (int)(146.0F + intensitySq * 50.0F);

            Pixels[pixelIndex * 4 + 0] = (byte)r;
            Pixels[pixelIndex * 4 + 1] = (byte)g;
            Pixels[pixelIndex * 4 + 2] = (byte)b;
            Pixels[pixelIndex * 4 + 3] = (byte)a;
        }
    }
}
