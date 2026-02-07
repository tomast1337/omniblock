using betareborn.Worlds;

namespace betareborn.Biomes
{
    public class BiomeGenRainforest : Biome
    {

        public override WorldGenerator getRandomWorldGenForTrees(java.util.Random var1)
        {
            return (WorldGenerator)(var1.nextInt(3) == 0 ? new WorldGenBigTree() : new WorldGenTrees());
        }
    }

}