using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

public abstract class Feature
{
    public abstract bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z);

    public virtual void prepare(double heightScale, double branchScale, double foliageScale)
    {
    }
}
