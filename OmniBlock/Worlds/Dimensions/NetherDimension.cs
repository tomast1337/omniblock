using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Generation;
using OmniBlock.Worlds.Generation.Biomes;
using OmniBlock.Worlds.Generation.Biomes.Source;
using Silk.NET.Maths;

namespace OmniBlock.Worlds.Dimensions;

internal class NetherDimension : Dimension
{
    private readonly DimensionGeneratorProfile _generatorProfile;

    internal NetherDimension(DimensionGeneratorProfile generatorProfile) => _generatorProfile = generatorProfile;

    public override bool HasWorldSpawn => false;

    public override void InitBiomeSource()
    {
        BiomeSource = new FixedBiomeSource(Biome.Hell, 1.0D, 0.0D);
        IsNether = true;
        EvaporatesWater = true;
        HasCeiling = true;
        Id = -1;
    }

    public override Vector3D<double> GetFogColor(float celestialAngle, float partialTicks) => new(0.2, 0.03, 0.03);

    protected override void InitBrightnessTable()
    {
        var offset = 0.1F;

        for (var i = 0; i <= 15; ++i)
        {
            var factor = 1.0F - i / 15.0F;
            LightLevelToLuminance[i] = (1.0F - factor) / (factor * 3.0F + 1.0F) * (1.0F - offset) + offset;
        }
    }

    public override IChunkSource CreateChunkGenerator()
    {
        WorldGeneratorBuildContext context = new(World, World.Seed, "");
        return _generatorProfile.CreateGenerator(context);
    }

    public override bool IsValidSpawnPoint(int x, int z)
    {
        var blockId = World.GetSpawnBlockId(x, z);
        return blockId != World.Content.Blocks.Get("omniblock:bedrock").Id
               && blockId != 0
               && World.Content.Blocks.IsOpaque(blockId);
    }

    public override float GetTimeOfDay(long time, float tickDelta) => 0.5F;
}
