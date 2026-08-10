using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Generation.Generators.Features;

namespace OmniBlock.Worlds.Generation.Biomes;

internal class BiomeGenTaiga : Biome
{
    public override Feature GetRandomWorldGenForTrees(JavaRandom rand) => rand.NextInt(3) == 0 ? new PineTreeFeature() : new SpruceTreeFeature();
}
