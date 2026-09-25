using OmniBlock.Worlds.Biomes.Source;

namespace OmniBlock.Worlds.Lod;

/// <summary>Resource-pack-independent climate used to tint distant terrain on the client.</summary>
public readonly record struct TerrainLodClimateSample(ushort Temperature, ushort Downfall)
{
    public double TemperatureValue => Temperature / (double)ushort.MaxValue;
    public double DownfallValue => Downfall / (double)ushort.MaxValue;

    public static TerrainLodClimateSample Average(
        TerrainLodClimateSample a, TerrainLodClimateSample b,
        TerrainLodClimateSample c, TerrainLodClimateSample d) => new(
        checked((ushort)((a.Temperature + b.Temperature + c.Temperature + d.Temperature + 2) / 4)),
        checked((ushort)((a.Downfall + b.Downfall + c.Downfall + d.Downfall + 2) / 4)));
}

/// <summary>Immutable X-major climate grid; never stores texture-pack colors in server caches.</summary>
public sealed class TerrainLodClimateGrid
{
    private readonly TerrainLodClimateSample[] _samples;

    public TerrainLodClimateGrid(int width, ReadOnlySpan<TerrainLodClimateSample> samples)
    {
        if (width <= 0 || samples.Length != checked(width * width))
            throw new ArgumentException("Climate samples must form a square grid.", nameof(samples));
        Width = width;
        _samples = samples.ToArray();
    }

    public int Width { get; }
    public TerrainLodClimateSample this[int x, int z] =>
        (uint)x < (uint)Width && (uint)z < (uint)Width
            ? _samples[x * Width + z]
            : throw new ArgumentOutOfRangeException(nameof(x));

    public static TerrainLodClimateGrid Capture(BiomeSource biomeSource, int chunkX, int chunkZ)
    {
        ArgumentNullException.ThrowIfNull(biomeSource);
        // BiomeSource keeps mutable scratch maps. A private clone prevents LOD workers from
        // racing simulation or generation while sampling the authoritative world climate.
        var source = biomeSource.Clone();
        source.GetBiomesInArea(checked(chunkX * 16), checked(chunkZ * 16), 16, 16);
        var samples = new TerrainLodClimateSample[256];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = new TerrainLodClimateSample(
                Quantize(source.TemperatureMap[index]), Quantize(source.DownfallMap[index]));
        return new TerrainLodClimateGrid(16, samples);
    }

    private static ushort Quantize(double value) =>
        !double.IsFinite(value)
            ? throw new InvalidDataException("Biome climate contains a non-finite value.")
            : checked((ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue));
}
