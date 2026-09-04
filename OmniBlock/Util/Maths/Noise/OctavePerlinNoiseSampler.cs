namespace OmniBlock.Util.Maths.Noise;

internal class OctavePerlinNoiseSampler : NoiseSampler
{
    private readonly int _octaveCount;

    private readonly PerlinNoiseSampler[] _octaves;

    public OctavePerlinNoiseSampler(JavaRandom rand, int octaveCount)
    {
        _octaveCount = octaveCount;
        _octaves = new PerlinNoiseSampler[octaveCount];

        for (var i = 0; i < octaveCount; ++i)
        {
            _octaves[i] = new PerlinNoiseSampler(rand);
        }
    }

    public double GenerateNoise(double x, double y)
    {
        var value = 0.0D;
        var amplitude = 1.0D;

        for (var i = 0; i < _octaveCount; ++i)
        {
            value += _octaves[i].GenerateNoise(x * amplitude, y * amplitude) / amplitude;
            amplitude /= 2.0D;
        }

        return value;
    }

    public double[] Create(double[] buffer, double xStart, double yStart, double zStart, int xSize, int ySize, int zSize, double xFrequency, double yFrequency, double zFrequency)
    {
        if (buffer == null)
        {
            buffer = new double[xSize * ySize * zSize];
        }
        else
        {
            Array.Fill(buffer, 0);
        }

        var octaveMultiplier = 1.0D;

        for (var i = 0; i < _octaveCount; ++i)
        {
            _octaves[i]
                .Sample(buffer,
                    xStart,
                    yStart,
                    zStart,
                    xSize,
                    ySize,
                    zSize,
                    xFrequency * octaveMultiplier,
                    yFrequency * octaveMultiplier,
                    zFrequency * octaveMultiplier,
                    octaveMultiplier);
            octaveMultiplier /= 2.0D;
        }

        return buffer;
    }

    // The last argument goes unused, but if it were used, it would definitely be that.
    public double[] Create(double[] buffer, int xStart, int zStart, int xSize, int zSize, double xFrequency, double zFrequency, double inverseAmplitude) => Create(buffer, xStart, 10.0D, zStart, xSize, 1, zSize, xFrequency, 1.0D, zFrequency);
}
