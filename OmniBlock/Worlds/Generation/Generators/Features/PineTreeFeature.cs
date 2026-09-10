using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class PineTreeFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        var treeHeight = rand.NextInt(5) + 7;
        var trunkWithNoLeaves = treeHeight - rand.NextInt(2) - 3;
        var canopyHeight = treeHeight - trunkWithNoLeaves;
        var maxLeafRadius = 1 + rand.NextInt(canopyHeight + 1);

        var canPlace = true;

        if (!(y >= 1 && y + treeHeight + 1 <= ChuckFormat.WorldHeight))
        {
            return false;
        }


        for (var cy = y; cy <= y + 1 + treeHeight && canPlace; ++cy)
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

            for (var cx = x - checkRadius; cx <= x + checkRadius && canPlace; ++cx)
            {
                for (var cz = z - checkRadius; cz <= z + checkRadius && canPlace; ++cz)
                {
                    if (cy >= 0 && cy < ChuckFormat.WorldHeight)
                    {
                        var blockId = level.Reader.GetBlockId(cx, cy, cz);
                        if (blockId != 0 && blockId != level.Content.Blocks.Get("leaves").Id)
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

        var groundId = level.Reader.GetBlockId(x, y - 1, z);
        if ((groundId == level.Content.Blocks.Get("grass_block").Id || groundId == level.Content.Blocks.Get("dirt").Id) && y < ChuckFormat.WorldHeight - treeHeight - 1)
        {
            level.Writer.SetBlockWithoutNotifyingNeighbors(x, y - 1, z, level.Content.Blocks.Get("dirt").Id, 0, false);
            var currentLeafRadius = 0;

            for (var cy = y + treeHeight; cy >= y + trunkWithNoLeaves; --cy)
            {
                for (var cx = x - currentLeafRadius; cx <= x + currentLeafRadius; ++cx)
                {
                    var offsetX = cx - x;

                    for (var cz = z - currentLeafRadius; cz <= z + currentLeafRadius; ++cz)
                    {
                        var offsetZ = cz - z;
                        if ((Math.Abs(offsetX) != currentLeafRadius || Math.Abs(offsetZ) != currentLeafRadius || currentLeafRadius <= 0) && !level.Content.Blocks.IsOpaque(level.Reader.GetBlockId(cx, cy, cz)))
                        {
                            level.Writer.SetBlockWithoutNotifyingNeighbors(cx, cy, cz, level.Content.Blocks.Get("leaves").Id, 1, false);
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

            for (var trunkY = 0; trunkY < treeHeight - 1; ++trunkY)
            {
                var blockAtTrunk = level.Reader.GetBlockId(x, y + trunkY, z);
                if (blockAtTrunk == 0 || blockAtTrunk == level.Content.Blocks.Get("leaves").Id)
                {
                    level.Writer.SetBlockWithoutNotifyingNeighbors(x, y + trunkY, z, level.Content.Blocks.Get("log").Id, 1, false);
                }
            }

            return true;
        }

        return false;
    }
}
