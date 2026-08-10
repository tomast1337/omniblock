using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Gen.Chunks;

public abstract class CommonChunkGenerator
{
    private protected readonly IWorldContext _world;
    private protected readonly JavaRandom _random;
    private protected readonly long _seed;

    public CommonChunkGenerator(IWorldContext world, long seed)
    {
        _world = world;
        _seed = seed;
        _random = new JavaRandom(seed);
    }
}
