using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Generation.Generators.Features;

namespace OmniBlock.Worlds.Generation.Biomes;

internal class BiomeGenForest : Biome
{
    public override Feature GetRandomWorldGenForTrees(JavaRandom rand) =>
        rand.NextInt(5) == 0 ? new BirchTreeFeature() :
        rand.NextInt(3) == 0 ? new LargeOakTreeFeature() :
        new OakTreeFeature();
}
