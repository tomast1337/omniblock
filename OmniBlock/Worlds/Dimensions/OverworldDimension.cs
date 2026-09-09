using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Gen.Chunks;
using OmniBlock.Worlds.Gen.Flat;
using OmniBlock.Worlds.Generation.Biomes;
using OmniBlock.Worlds.Generation.Biomes.Source;
using OmniBlock.Worlds.Generation;

namespace OmniBlock.Worlds.Dimensions;

internal class OverworldDimension : Dimension
{
    public override float CloudHeight => World.Properties.TerrainType.Key == WorldType.Sky.Key ? 8.0F : base.CloudHeight;

    public override void InitBiomeSource()
    {
        if (World.Properties.TerrainType.Key == WorldType.Sky.Key)
        {
            BiomeSource = new FixedBiomeSource(Biome.Sky, 0.5D, 0.0D);
            return;
        }

        base.InitBiomeSource();
    }

    public override IChunkSource CreateChunkGenerator()
    {
        var terrainType = World.Properties.TerrainType;
        WorldGeneratorBuildContext context = new(
            World,
            World.Seed,
            World.Properties.GeneratorOptions);
        return World.Content.WorldGeneratorProviders.Create(
            terrainType.GeneratorProviderType,
            context);
    }

    public override bool IsValidSpawnPoint(int x, int z)
    {
        if (World.Properties.TerrainType.Key == WorldType.Flat.Key)
        {
            return true;
        }

        if (World.Properties.TerrainType.Key == WorldType.Sky.Key)
        {
            var topSolidY = World.Reader.GetTopSolidBlockY(x, z);
            if (topSolidY <= 0) return false;
            var blockId = World.Reader.GetBlockId(x, topSolidY - 1, z);
            return blockId != 0
                   && World.Content.Blocks.TryGetByProtocolId(blockId, out var block)
                   && block.Material.BlocksMovement;
        }

        return base.IsValidSpawnPoint(x, z);
    }

    public override float GetTimeOfDay(long time, float partialTicks)
    {
        if (World.Properties.TerrainType.Key == WorldType.Sky.Key)
        {
            return 0.0F;
        }

        return base.GetTimeOfDay(time, partialTicks);
    }
}
