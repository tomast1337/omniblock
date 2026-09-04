namespace OmniBlock.Util.Maths.Noise;

internal class PerlinNoiseSampler : NoiseSampler
{
    private readonly int[] _permutations;
    private readonly double _xCoord;
    private readonly double _yCoord;
    private readonly double _zCoord;

    public PerlinNoiseSampler() : this(new JavaRandom())
    {
    }

    public PerlinNoiseSampler(JavaRandom rand)
    {
        _permutations = new int[512];
        _xCoord = rand.NextDouble() * 256.0D;
        _yCoord = rand.NextDouble() * 256.0D;
        _zCoord = rand.NextDouble() * 256.0D;

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

    private double GenerateNoise(double x, double y, double z)
    {
        x += _xCoord;
        y += _yCoord;
        z += _zCoord;
        var xInt = (int)x;
        var yInt = (int)y;
        var zInt = (int)z;
        if (x < xInt)
        {
            --xInt;
        }

        if (y < yInt)
        {
            --yInt;
        }

        if (z < zInt)
        {
            --zInt;
        }

        var xMod255 = xInt & 255;
        var yMod255 = yInt & 255;
        var zMod255 = zInt & 255;
        x -= xInt;
        y -= yInt;
        z -= zInt;
        var sX = x * x * x * (x * (x * 6.0D - 15.0D) + 10.0D);
        var sY = y * y * y * (y * (y * 6.0D - 15.0D) + 10.0D);
        var sZ = z * z * z * (z * (z * 6.0D - 15.0D) + 10.0D);
        var a = _permutations[xMod255] + yMod255;
        var aa = _permutations[a] + zMod255;
        var ab = _permutations[a + 1] + zMod255;
        var b = _permutations[xMod255 + 1] + yMod255;
        var ba = _permutations[b] + zMod255;
        var bb = _permutations[b + 1] + zMod255;
        return Lerp(sZ, Lerp(sY, Lerp(sX, Grad(_permutations[aa], x, y, z),
                    Grad(_permutations[ba], x - 1, y, z)),
                Lerp(sX, Grad(_permutations[ab], x, y - 1, z),
                    Grad(_permutations[bb], x - 1, y - 1, z))),
            Lerp(sY, Lerp(sX, Grad(_permutations[aa + 1], x, y, z - 1),
                    Grad(_permutations[ba + 1], x - 1, y, z - 1)),
                Lerp(sX, Grad(_permutations[ab + 1], x, y - 1, z - 1),
                    Grad(_permutations[bb + 1], x - 1, y - 1, z - 1))));
    }

    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Grad(int hash, double x, double y)
    {
        var h = hash & 15;
        var u = (1 - ((h & 8) >> 3)) * x;
        var v = h < 4 ? 0.0D : h != 12 && h != 14 ? y : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static double Grad(int hash, double x, double y, double z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h != 12 && h != 14 ? z : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    public double GenerateNoise(double x, double y) => GenerateNoise(x, y, 0.0D);

    public void Sample(double[] buffer,
        double xStart,
        double yStart,
        double zStart,
        int xSize,
        int ySize,
        int zSize,
        double xFrequency,
        double yFrequency,
        double zFrequency,
        double inverseAmplitude)
    {
        var counter = 0;
        var amplitude = 1.0D / inverseAmplitude;

        if (ySize == 1) // 2d (xz)
        {
            for (var x = 0; x < xSize; ++x)
            {
                var xCoord = (xStart + x) * xFrequency + _xCoord;
                var xCoordInt = (int)xCoord;
                if (xCoord < xCoordInt)
                {
                    --xCoordInt;
                }

                var xMod255 = xCoordInt & 255;
                xCoord -= xCoordInt;

                var xFinal = xCoord * xCoord * xCoord * (xCoord * (xCoord * 6.0D - 15.0D) + 10.0D);

                for (var z = 0; z < zSize; ++z)
                {
                    var zCoord = (zStart + z) * zFrequency + _zCoord;
                    var zCoordInt = (int)zCoord;
                    if (zCoord < zCoordInt)
                    {
                        --zCoordInt;
                    }

                    var zMod255 = zCoordInt & 255;
                    zCoord -= zCoordInt;

                    var zFinal = zCoord * zCoord * zCoord * (zCoord * (zCoord * 6.0D - 15.0D) + 10.0D);

                    var aa = _permutations[xMod255];
                    var ab = _permutations[aa] + zMod255;
                    var ba = _permutations[xMod255 + 1];
                    var bb = _permutations[ba] + zMod255;
                    var xLerpZ0 = Lerp(xFinal, Grad(_permutations[ab], xCoord, zCoord),
                        Grad(_permutations[bb], xCoord - 1, 0, zCoord));
                    var xLerpZ1 = Lerp(xFinal, Grad(_permutations[ab + 1], xCoord, 0, zCoord - 1),
                        Grad(_permutations[bb + 1], xCoord - 1, 0, zCoord - 1));
                    var finalNoise = Lerp(zFinal, xLerpZ0, xLerpZ1);
                    buffer[counter++] += finalNoise * amplitude;
                }
            }
        }
        else
        {
            var oldY = -1;
            // Don't move these inside the loop
            var xLerpY0Z0 = 0.0D;
            var xLerpY1Z0 = 0.0D;
            var xLerpY0Z1 = 0.0D;
            var xLerpY1Z1 = 0.0D;

            for (var x = 0; x < xSize; ++x)
            {
                var xCoord = (xStart + x) * xFrequency + _xCoord;
                var xCoordInt = (int)xCoord;
                if (xCoord < xCoordInt)
                {
                    --xCoordInt;
                }

                var xMod255 = xCoordInt & 255;
                xCoord -= xCoordInt;

                var xFinal = xCoord * xCoord * xCoord * (xCoord * (xCoord * 6.0D - 15.0D) + 10.0D);

                for (var z = 0; z < zSize; ++z)
                {
                    var zCoord = (zStart + z) * zFrequency + _zCoord;
                    var zCoordInt = (int)zCoord;
                    if (zCoord < zCoordInt)
                    {
                        --zCoordInt;
                    }

                    var zMod255 = zCoordInt & 255;
                    zCoord -= zCoordInt;

                    var zFinal = zCoord * zCoord * zCoord * (zCoord * (zCoord * 6.0D - 15.0D) + 10.0D);

                    for (var y = 0; y < ySize; ++y)
                    {
                        var yCoord = (yStart + y) * yFrequency + _yCoord;
                        var yCoordInt = (int)yCoord;
                        if (yCoord < yCoordInt)
                        {
                            --yCoordInt;
                        }

                        var yMod255 = yCoordInt & 255;
                        yCoord -= yCoordInt;

                        var yFinal = yCoord * yCoord * yCoord * (yCoord * (yCoord * 6.0D - 15.0D) + 10.0D);

                        if (y == 0 || yMod255 != oldY)
                        {
                            oldY = yMod255;
                            var a = _permutations[xMod255] + yMod255;
                            var aa = _permutations[a] + zMod255;
                            var ab = _permutations[a + 1] + zMod255;
                            var b = _permutations[xMod255 + 1] + yMod255;
                            var ba = _permutations[b] + zMod255;
                            var bb = _permutations[b + 1] + zMod255;
                            xLerpY0Z0 = Lerp(xFinal,
                                Grad(_permutations[aa], xCoord, yCoord, zCoord),
                                Grad(_permutations[ba], xCoord - 1, yCoord, zCoord));
                            xLerpY1Z0 = Lerp(xFinal,
                                Grad(_permutations[ab], xCoord, yCoord - 1, zCoord),
                                Grad(_permutations[bb], xCoord - 1, yCoord - 1, zCoord));
                            xLerpY0Z1 = Lerp(xFinal,
                                Grad(_permutations[aa + 1], xCoord, yCoord, zCoord - 1),
                                Grad(_permutations[ba + 1], xCoord - 1, yCoord, zCoord - 1));
                            xLerpY1Z1 = Lerp(xFinal,
                                Grad(_permutations[ab + 1], xCoord, yCoord - 1, zCoord - 1),
                                Grad(_permutations[bb + 1], xCoord - 1, yCoord - 1, zCoord - 1));
                        }

                        var yLerpZ0 = Lerp(yFinal, xLerpY0Z0, xLerpY1Z0);
                        var yLerpZ1 = Lerp(yFinal, xLerpY0Z1, xLerpY1Z1);
                        var finalNoise = Lerp(zFinal, yLerpZ0, yLerpZ1);
                        buffer[counter++] += finalNoise * amplitude;
                    }
                }
            }
        }
    }
}
