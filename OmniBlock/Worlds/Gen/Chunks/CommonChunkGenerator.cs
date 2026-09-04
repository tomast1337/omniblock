using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Gen.Chunks;

public abstract class CommonChunkGenerator
{
    private protected readonly JavaRandom _random;
    private protected readonly long _seed;
    private protected readonly IWorldContext _world;

    public CommonChunkGenerator(IWorldContext world, long seed)
    {
        _world = world;
        _seed = seed;
        _random = new JavaRandom(seed);
    }
}
