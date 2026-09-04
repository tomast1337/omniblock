using OmniBlock.Util.Maths;
using OmniBlock.Util.Maths.Noise;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Generation.Biomes;

namespace OmniBlock.Worlds.Biomes.Source;

public class BiomeSource
{
    private readonly OctaveSimplexNoiseSampler _downfallSampler;
    private readonly OctaveSimplexNoiseSampler _temperatureSampler;
    private readonly OctaveSimplexNoiseSampler _weirdnessSampler;
    public Biome[] Biomes;
    public double[] DownfallMap;
    public double[] TemperatureMap;
    public double[] WeirdnessMap;

    protected BiomeSource()
    {
    }

    public BiomeSource(IWorldContext world)
    {
        _temperatureSampler = new OctaveSimplexNoiseSampler(new JavaRandom(world.Seed * 9871L), 4);
        _downfallSampler = new OctaveSimplexNoiseSampler(new JavaRandom(world.Seed * 39811L), 4);
        _weirdnessSampler = new OctaveSimplexNoiseSampler(new JavaRandom(world.Seed * 543321L), 2);
    }

    public BiomeSource(BiomeSource other)
    {
        _temperatureSampler = other._temperatureSampler;
        _downfallSampler = other._downfallSampler;
        _weirdnessSampler = other._weirdnessSampler;
    }

    public virtual Biome GetBiome(ChunkPos chunkPos) => GetBiome(chunkPos.X << 4, chunkPos.Z << 4);

    public virtual Biome GetBiome(int x, int z) => GetBiomesInArea(x, z, 1, 1)[0];

    public virtual double GetTemperature(int x, int z)
    {
        TemperatureMap = _temperatureSampler.Sample(TemperatureMap, x, z, 1, 1, 0.025F, 0.025F, 0.5D);
        return TemperatureMap[0];
    }

    public virtual Biome[] GetBiomesInArea(int x, int z, int width, int depth)
    {
        Biomes = GetBiomesInArea(Biomes, x, z, width, depth);
        return Biomes;
    }

    public virtual double[] GetTemperatures(double[] map, int x, int z, int width, int depth)
    {
        var size = width * depth;
        if (map == null || map.Length < size)
        {
            map = new double[size];
        }

        map = _temperatureSampler.Sample(map, x, z, width, depth, 0.025F, 0.025F, 0.25D);
        WeirdnessMap = _weirdnessSampler.Sample(WeirdnessMap, x, z, width, depth, 0.25D, 0.25D, 10 / 17d);
        var index = 0;

        for (var i = 0; i < width; ++i)
        {
            for (var j = 0; j < depth; ++j)
            {
                var weirdness = WeirdnessMap[index] * 1.1D + 0.5D;
                var weight = 0.01D;
                var oneMinusWeight = 1.0D - weight;
                var temperature = (map[index] * 0.15D + 0.7D) * oneMinusWeight + weirdness * weight;
                temperature = 1.0D - (1.0D - temperature) * (1.0D - temperature);
                if (temperature < 0.0D)
                {
                    temperature = 0.0D;
                }

                if (temperature > 1.0D)
                {
                    temperature = 1.0D;
                }

                map[index] = temperature;
                ++index;
            }
        }

        return map;
    }

    public virtual Biome[] GetBiomesInArea(Biome[] biomes, int x, int z, int width, int depth)
    {
        var size = width * depth;
        if (biomes == null || biomes.Length < size)
        {
            biomes = new Biome[size];
        }

        TemperatureMap = _temperatureSampler.Sample(TemperatureMap, x, z, width, width, 0.025F, 0.025F, 0.25D);
        DownfallMap = _downfallSampler.Sample(DownfallMap, x, z, width, width, 0.05F, 0.05F, 1.0D / 3.0D);
        WeirdnessMap = _weirdnessSampler.Sample(WeirdnessMap, x, z, width, width, 0.25D, 0.25D, 0.5882352941176471D);
        var index = 0;

        for (var i = 0; i < width; ++i)
        {
            for (var j = 0; j < depth; ++j)
            {
                var weirdness = WeirdnessMap[index] * 1.1D + 0.5D;
                var weight = 0.01D;
                var oneMinusWeight = 1.0D - weight;
                var temperature = (TemperatureMap[index] * 0.15D + 0.7D) * oneMinusWeight + weirdness * weight;
                weight = 0.002D;
                oneMinusWeight = 1.0D - weight;
                var downfall = (DownfallMap[index] * 0.15D + 0.5D) * oneMinusWeight + weirdness * weight;
                temperature = 1.0D - (1.0D - temperature) * (1.0D - temperature);
                if (temperature < 0.0D)
                {
                    temperature = 0.0D;
                }

                if (downfall < 0.0D)
                {
                    downfall = 0.0D;
                }

                if (temperature > 1.0D)
                {
                    temperature = 1.0D;
                }

                if (downfall > 1.0D)
                {
                    downfall = 1.0D;
                }

                TemperatureMap[index] = temperature;
                DownfallMap[index] = downfall;
                biomes[index++] = Biome.GetBiome(temperature, downfall);
            }
        }

        return biomes;
    }

    public virtual BiomeSource Clone() => new(this);
}
