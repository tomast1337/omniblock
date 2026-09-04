using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class GrassPatchFeature : Feature
{
    private readonly int _tallGrassBlockId;
    private readonly int _tallGrassBlockMeta;

    public GrassPatchFeature(int tallGrassBlockId, int tallGrassBlockMeta)
    {
        _tallGrassBlockId = tallGrassBlockId;
        _tallGrassBlockMeta = tallGrassBlockMeta;
    }

    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        while (true)
        {
            var blockId = level.Reader.GetBlockId(x, y, z);
            if ((blockId != 0 && blockId != level.Content.Blocks.Get("leaves").Id) || y <= 0)
            {
                for (var i = 0; i < 128; ++i)
                {
                    var genX = x + rand.NextInt(8) - rand.NextInt(8);
                    var genY = y + rand.NextInt(4) - rand.NextInt(4);
                    var genZ = z + rand.NextInt(8) - rand.NextInt(8);
                    if (level.Reader.IsAir(genX, genY, genZ) &&
                        level.Content.Blocks.GetByProtocolId(_tallGrassBlockId).CanGrow(new OnTickEvent(level, genX, genY, genZ, level.Reader.GetBlockMeta(genX, genY, genZ), level.Reader.GetBlockId(genX, genY, genZ))))
                    {
                        level.Writer.SetBlockWithoutNotifyingNeighbors(genX, genY, genZ, _tallGrassBlockId, _tallGrassBlockMeta, false);
                    }
                }

                return true;
            }

            --y;
        }
    }
}
