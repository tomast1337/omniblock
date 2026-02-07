using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Biomes
{
    public class BiomeGenForest : Biome
    {

        public BiomeGenForest()
        {
            spawnableCreatureList.add(new SpawnListEntry(EntityWolf.Class, 2));
        }

        public override WorldGenerator getRandomWorldGenForTrees(java.util.Random var1)
        {
            return (WorldGenerator)(var1.nextInt(5) == 0 ? new WorldGenForest() : (var1.nextInt(3) == 0 ? new WorldGenBigTree() : new WorldGenTrees()));
        }
    }

}