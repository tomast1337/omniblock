namespace OmniBlock.Client.Rendering.Core.Textures.Atlas;

/// <summary>
///     Filters one named atlas tile at a time. Alpha is premultiplied during averaging so a
///     transparent texel cannot turn the edge of a cutout texture dark; RGB is averaged in linear
///     light so the distant representative color does not become artificially muddy.
/// </summary>
internal static class TerrainTileMipmaps
{
    private static readonly float[] s_linear = Enumerable.Range(0, 256)
        .Select(static value => SrgbToLinear(value / 255f)).ToArray();

    public static byte[][] Build(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA tile size does not match its dimensions.", nameof(rgba));

        List<byte[]> levels = [rgba.ToArray()];
        while (width > 1 || height > 1)
        {
            var nextWidth = Math.Max(1, width / 2);
            var nextHeight = Math.Max(1, height / 2);
            var source = levels[^1];
            var filtered = new byte[checked(nextWidth * nextHeight * 4)];
            for (var y = 0; y < nextHeight; y++)
            for (var x = 0; x < nextWidth; x++)
            {
                var x0 = x * width / nextWidth;
                var x1 = (x + 1) * width / nextWidth;
                var y0 = y * height / nextHeight;
                var y1 = (y + 1) * height / nextHeight;
                var alphaSum = 0f;
                var red = 0f;
                var green = 0f;
                var blue = 0f;
                for (var sourceY = y0; sourceY < y1; sourceY++)
                for (var sourceX = x0; sourceX < x1; sourceX++)
                {
                    var pixel = (sourceY * width + sourceX) * 4;
                    var alpha = source[pixel + 3] / 255f;
                    alphaSum += alpha;
                    red += s_linear[source[pixel]] * alpha;
                    green += s_linear[source[pixel + 1]] * alpha;
                    blue += s_linear[source[pixel + 2]] * alpha;
                }

                var target = (y * nextWidth + x) * 4;
                filtered[target + 3] = ToByte(alphaSum / ((x1 - x0) * (y1 - y0)));
                if (alphaSum <= 0f) continue;
                filtered[target] = LinearToSrgbByte(red / alphaSum);
                filtered[target + 1] = LinearToSrgbByte(green / alphaSum);
                filtered[target + 2] = LinearToSrgbByte(blue / alphaSum);
            }
            levels.Add(filtered);
            width = nextWidth;
            height = nextHeight;
        }
        return [.. levels];
    }

    private static float SrgbToLinear(float color) => color <= 0.04045f
        ? color / 12.92f
        : MathF.Pow((color + 0.055f) / 1.055f, 2.4f);

    private static byte LinearToSrgbByte(float color) => ToByte(color <= 0.0031308f
        ? color * 12.92f
        : 1.055f * MathF.Pow(color, 1f / 2.4f) - 0.055f);

    private static byte ToByte(float unit) => (byte)Math.Clamp(
        (int)MathF.Round(unit * 255f), 0, 255);
}
