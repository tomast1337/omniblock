using betareborn.Worlds;
using betareborn.Worlds.Gen;
using betareborn.Worlds.Gen.Features;

namespace betareborn.Worlds.Biomes
{
    public class BiomeGenRainforest : Biome
    {

        public override Feature getRandomWorldGenForTrees(java.util.Random var1)
        {
            return var1.nextInt(3) == 0 ? new LargeOakTreeFeature() : new OakTreeFeature();
        }
    }

}