using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Generation.Generators.Features;

namespace BetaSharp.Worlds.Generation.Biomes;

internal class BiomeGenTaiga : Biome
{
    public override Feature GetRandomWorldGenForTrees(JavaRandom rand) => rand.NextInt(3) == 0 ? new PineTreeFeature() : new SpruceTreeFeature();
}
