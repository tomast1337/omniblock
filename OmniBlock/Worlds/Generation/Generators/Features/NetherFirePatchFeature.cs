using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class NetherFirePatchFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        for (int i = 0; i < 64; ++i)
        {
            int genX = x + rand.NextInt(8) - rand.NextInt(8);
            int genY = y + rand.NextInt(4) - rand.NextInt(4);
            int genZ = z + rand.NextInt(8) - rand.NextInt(8);
            if (level.Reader.IsAir(genX, genY, genZ) && level.Reader.GetBlockId(genX, genY - 1, genZ) == level.Content.Blocks.Get("netherrack").Id)
            {
                level.Writer.SetBlock(genX, genY, genZ, level.Content.Blocks.Get("fire").Id, 0, false);
            }
        }

        return true;
    }
}
