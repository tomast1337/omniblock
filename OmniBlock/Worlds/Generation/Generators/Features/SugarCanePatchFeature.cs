using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class SugarCanePatchFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        for (var i = 0; i < 20; ++i)
        {
            var genX = x + rand.NextInt(4) - rand.NextInt(4);
            var genZ = z + rand.NextInt(4) - rand.NextInt(4);

            if (!level.Reader.IsAir(genX, y, genZ))
            {
                continue;
            }

            var hasWaterNearby = level.Reader.GetMaterial(genX - 1, y - 1, genZ) == Material.Water ||
                                 level.Reader.GetMaterial(genX + 1, y - 1, genZ) == Material.Water ||
                                 level.Reader.GetMaterial(genX, y - 1, genZ - 1) == Material.Water ||
                                 level.Reader.GetMaterial(genX, y - 1, genZ + 1) == Material.Water;

            if (hasWaterNearby)
            {
                var height = 2 + rand.NextInt(rand.NextInt(3) + 1);

                for (var h = 0; h < height; ++h)
                {
                    if (level.Content.Blocks.Get("sugar_cane").CanGrow(new OnTickEvent(level, genX, y + h, genZ, level.Reader.GetBlockMeta(genX, y + h, genZ), level.Reader.GetBlockId(genX, y + h, genZ))))
                    {
                        level.Writer.SetBlock(genX, y + h, genZ, level.Content.Blocks.Get("sugar_cane").Id, 0, false);
                    }
                }
            }
        }

        return true;
    }
}
