using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Biomes
{
    public class BiomeGenTaiga : Biome
    {

        public BiomeGenTaiga()
        {
            spawnableCreatureList.add(new SpawnListEntry(EntityWolf.Class, 2));
        }

        public override WorldGenerator getRandomWorldGenForTrees(java.util.Random var1)
        {
            return (WorldGenerator)(var1.nextInt(3) == 0 ? new WorldGenTaiga1() : new WorldGenTaiga2());
        }
    }

}