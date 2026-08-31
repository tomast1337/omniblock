using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class PineTreeFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        int treeHeight = rand.NextInt(5) + 7;
        int trunkWithNoLeaves = treeHeight - rand.NextInt(2) - 3;
        int canopyHeight = treeHeight - trunkWithNoLeaves;
        int maxLeafRadius = 1 + rand.NextInt(canopyHeight + 1);

        bool canPlace = true;

        if (!(y >= 1 && y + treeHeight + 1 <= ChuckFormat.WorldHeight))
        {
            return false;
        }


        for (int cy = y; cy <= y + 1 + treeHeight && canPlace; ++cy)
        {
            int checkRadius;
            if (cy - y < trunkWithNoLeaves)
            {
                checkRadius = 0;
            }
            else
            {
                checkRadius = maxLeafRadius;
            }

            for (int cx = x - checkRadius; cx <= x + checkRadius && canPlace; ++cx)
            {
                for (int cz = z - checkRadius; cz <= z + checkRadius && canPlace; ++cz)
                {
                    if (cy >= 0 && cy < ChuckFormat.WorldHeight)
                    {
                        int blockId = level.Reader.GetBlockId(cx, cy, cz);
                        if (blockId != 0 && blockId != BlockRegistry.Get("leaves").Id)
                        {
                            canPlace = false;
                        }
                    }
                    else
                    {
                        canPlace = false;
                    }
                }
            }
        }

        if (!canPlace)
        {
            return false;
        }

        int groundId = level.Reader.GetBlockId(x, y - 1, z);
        if ((groundId == BlockRegistry.Get("grass_block").Id || groundId == BlockRegistry.Get("dirt").Id) && y < ChuckFormat.WorldHeight - treeHeight - 1)
        {
            level.Writer.SetBlockWithoutNotifyingNeighbors(x, y - 1, z, BlockRegistry.Get("dirt").Id, 0, false);
            int currentLeafRadius = 0;

            for (int cy = y + treeHeight; cy >= y + trunkWithNoLeaves; --cy)
            {
                for (int cx = x - currentLeafRadius; cx <= x + currentLeafRadius; ++cx)
                {
                    int offsetX = cx - x;

                    for (int cz = z - currentLeafRadius; cz <= z + currentLeafRadius; ++cz)
                    {
                        int offsetZ = cz - z;
                        if ((Math.Abs(offsetX) != currentLeafRadius || Math.Abs(offsetZ) != currentLeafRadius || currentLeafRadius <= 0) && !BlockRegistry.IsOpaque(level.Reader.GetBlockId(cx, cy, cz)))
                        {
                            level.Writer.SetBlockWithoutNotifyingNeighbors(cx, cy, cz, BlockRegistry.Get("leaves").Id, 1, false);
                        }
                    }
                }

                if (currentLeafRadius >= 1 && cy == y + trunkWithNoLeaves + 1)
                {
                    --currentLeafRadius;
                }
                else if (currentLeafRadius < maxLeafRadius)
                {
                    ++currentLeafRadius;
                }
            }

            ;

            for (int trunkY = 0; trunkY < treeHeight - 1; ++trunkY)
            {
                int blockAtTrunk = level.Reader.GetBlockId(x, y + trunkY, z);
                if (blockAtTrunk == 0 || blockAtTrunk == BlockRegistry.Get("leaves").Id)
                {
                    level.Writer.SetBlockWithoutNotifyingNeighbors(x, y + trunkY, z, BlockRegistry.Get("log").Id, 1, false);
                }
            }

            return true;
        }

        return false;
    }
}
