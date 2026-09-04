namespace OmniBlock.Util.Maths.Noise;

internal class SimplexNoiseSampler
{
    private static readonly (int, int)[] s_grads = [(1, 1), (-1, 1), (1, -1), (-1, -1), (1, 0), (-1, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (0, 1), (0, -1)];
    private static readonly double F2 = 0.5D * (Math.Sqrt(3.0D) - 1.0D);
    private static readonly double G2 = (3.0D - Math.Sqrt(3.0D)) / 6.0D;

    private readonly int[] _permutations;
    private readonly double _xCoord;
    private readonly double _yCoord;

    public SimplexNoiseSampler() : this(new JavaRandom())
    {
    }

    public SimplexNoiseSampler(JavaRandom rand)
    {
        _permutations = new int[512];
        _xCoord = rand.NextDouble() * 256.0D;
        _yCoord = rand.NextDouble() * 256.0D;
        _ = rand.NextDouble();

        // Fill perm with values from 0 to 255 in random order, duplicating the first 256 values to the end of the array
        for (var i = 0; i < 256; i++)
        {
            _permutations[i] = i;
        }

        for (var i = 0; i < 256; ++i)
        {
            var j = rand.NextInt(256 - i) + i;
            (_permutations[i], _permutations[j]) = (_permutations[j], _permutations[i]);
            _permutations[i + 256] = _permutations[i];
        }
    }

    private static double Dot((int x, int y) grad, double dx, double dy) => grad.x * dx + grad.y * dy;

    public void Sample(double[] buffer, double x, double z, int width, int depth, double xFrequency, double zFrequency, double amplitude)
    {
        var counter = 0;

        for (var x1 = 0; x1 < width; ++x1)
        {
            var x2 = (x + x1) * xFrequency + _xCoord;

            for (var z1 = 0; z1 < depth; ++z1)
            {
                var z2 = (z + z1) * zFrequency + _yCoord;
                var s = (x2 + z2) * F2;
                var i = MathHelper.Floor(x2 + s);
                var j = MathHelper.Floor(z2 + s);
                var t = (i + j) * G2;
                var x3 = i - t;
                var z3 = j - t;
                var x4 = x2 - x3;
                var z4 = z2 - z3;
                byte i1;
                byte j1;
                if (x4 > z4)
                {
                    i1 = 1;
                    j1 = 0;
                }
                else
                {
                    i1 = 0;
                    j1 = 1;
                }

                var x5 = x4 - i1 + G2;
                var z5 = z4 - j1 + G2;
                var x6 = x4 - 1.0D + 2.0D * G2;
                var z6 = z4 - 1.0D + 2.0D * G2;
                var ii = i & 255;
                var jj = j & 255;
                var gi0 = _permutations[ii + _permutations[jj]] % 12;
                var gi1 = _permutations[ii + i1 + _permutations[jj + j1]] % 12;
                var gi2 = _permutations[ii + 1 + _permutations[jj + 1]] % 12;
                var t0 = 0.5D - x4 * x4 - z4 * z4;
                double n0;
                if (t0 < 0.0D)
                {
                    n0 = 0.0D;
                }
                else
                {
                    t0 *= t0;
                    n0 = t0 * t0 * Dot(s_grads[gi0], x4, z4);
                }

                var t1 = 0.5D - x5 * x5 - z5 * z5;
                double n1;
                if (t1 < 0.0D)
                {
                    n1 = 0.0D;
                }
                else
                {
                    t1 *= t1;
                    n1 = t1 * t1 * Dot(s_grads[gi1], x5, z5);
                }

                var t2 = 0.5D - x6 * x6 - z6 * z6;
                double n2;
                if (t2 < 0.0D)
                {
                    n2 = 0.0D;
                }
                else
                {
                    t2 *= t2;
                    n2 = t2 * t2 * Dot(s_grads[gi2], x6, z6);
                }

                buffer[counter++] += 70.0D * (n0 + n1 + n2) * amplitude;
            }
        }
    }
}
