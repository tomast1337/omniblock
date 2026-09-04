using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class SpruceTreeFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        var totalHeight = rand.NextInt(4) + 6;
        var topTrunkNoLeaves = 1 + rand.NextInt(2);
        var leafStartOffset = totalHeight - topTrunkNoLeaves;
        var maxLeafRadius = 2 + rand.NextInt(2);

        var canPlace = true;

        if (!(y >= 1 && y + totalHeight + 1 <= ChuckFormat.WorldHeight))
        {
            return false;
        }

        for (var cy = y; cy <= y + 1 + totalHeight && canPlace; ++cy)
        {
            int checkRadius;
            if (cy - y < topTrunkNoLeaves)
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
        if (!((groundId == level.Content.Blocks.Get("grass_block").Id || groundId == level.Content.Blocks.Get("dirt").Id) && y < ChuckFormat.WorldHeight - totalHeight - 1))
        {
            return false;
        }

        level.Writer.SetBlockWithoutNotifyingNeighbors(x, y - 1, z, level.Content.Blocks.Get("dirt").Id, 0, false);
        var currentRadius = rand.NextInt(2);
        var radiusTarget = 1;
        byte radiusStep = 0;


        for (var h = 0; h <= leafStartOffset; ++h)
        {
            var leafY = y + totalHeight - h;

            for (var cx = x - currentRadius; cx <= x + currentRadius; ++cx)
            {
                var offsetX = cx - x;
                for (var cz = z - currentRadius; cz <= z + currentRadius; ++cz)
                {
                    var offsetZ = cz - z;

                    if ((Math.Abs(offsetX) != currentRadius || Math.Abs(offsetZ) != currentRadius || currentRadius <= 0) && !level.Content.Blocks.IsOpaque(level.Reader.GetBlockId(cx, leafY, cz)))
                    {
                        level.Writer.SetBlockWithoutNotifyingNeighbors(cx, leafY, cz, level.Content.Blocks.Get("leaves").Id, 1, false);
                    }
                }
            }

            if (currentRadius >= radiusTarget)
            {
                currentRadius = radiusStep;
                radiusStep = 1;
                ++radiusTarget;
                if (radiusTarget > maxLeafRadius)
                {
                    radiusTarget = maxLeafRadius;
                }
            }
            else
            {
                ++currentRadius;
            }
        }

        var trunkVariability = rand.NextInt(3);

        for (var trunkY = 0; trunkY < totalHeight - trunkVariability; ++trunkY)
        {
            var blockAtTrunk = level.Reader.GetBlockId(x, y + trunkY, z);
            if (blockAtTrunk == 0 || blockAtTrunk == level.Content.Blocks.Get("leaves").Id)
            {
                level.Writer.SetBlockWithoutNotifyingNeighbors(x, y + trunkY, z, level.Content.Blocks.Get("log").Id, 1, false);
            }
        }

        return true;
    }
}
