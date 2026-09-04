using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class CactusPatchFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        for (var i = 0; i < 10; ++i)
        {
            var genX = x + rand.NextInt(8) - rand.NextInt(8);
            var genY = y + rand.NextInt(4) - rand.NextInt(4);
            var genZ = z + rand.NextInt(8) - rand.NextInt(8);
            if (level.Reader.IsAir(genX, genY, genZ))
            {
                var height = 1 + rand.NextInt(rand.NextInt(3) + 1);

                for (var h = 0; h < height; ++h)
                {
                    if (level.Content.Blocks.Get("cactus").CanGrow(new OnTickEvent(level, genX, genY + h, genZ, level.Reader.GetBlockMeta(genX, genY + h, genZ), level.Reader.GetBlockId(genX, genY + h, genZ))))
                    {
                        level.Writer.SetBlockWithoutNotifyingNeighbors(genX, genY + h, genZ, level.Content.Blocks.Get("cactus").Id, 0, false);
                    }
                }
            }
        }

        return true;
    }
}
