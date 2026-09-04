using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Gen.Chunks;
using OmniBlock.Worlds.Generation.Biomes;
using OmniBlock.Worlds.Generation.Biomes.Source;

namespace OmniBlock.Worlds.Dimensions;

public class SkyDimension : Dimension
{
    public override float CloudHeight => 8.0F;

    public override void InitBiomeSource() => BiomeSource = new FixedBiomeSource(Biome.Sky, 0.5D, 0.0D);

    public override IChunkSource CreateChunkGenerator() => new SkyChunkGenerator(World, World.Seed);

    public override float GetTimeOfDay(long time, float partialTicks) => 0.0F;

    public override bool IsValidSpawnPoint(int x, int y)
    {
        var topBlockId = World.GetSpawnBlockId(x, y);
        return topBlockId != 0
               && World.Content.Blocks.TryGetByProtocolId(topBlockId, out var block)
               && block.Material.BlocksMovement;
    }
}
