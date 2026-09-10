using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class BirchTreeFeature : Feature
{
    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        var treeHeight = rand.NextInt(3) + 5;
        var canPlace = true;
        if (!(y >= 1 && y + treeHeight + 1 <= ChuckFormat.WorldHeight))
        {
            return false;
        }


        for (var cy = y; cy <= y + 1 + treeHeight; ++cy)
        {
            byte checkRadius = 1;
            if (cy == y)
            {
                checkRadius = 0;
            }

            if (cy >= y + 1 + treeHeight - 2)
            {
                checkRadius = 2;
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

        var soilId = level.Reader.GetBlockId(x, y - 1, z);
        if ((soilId == level.Content.Blocks.Get("grass_block").Id || soilId == level.Content.Blocks.Get("dirt").Id) && y < ChuckFormat.WorldHeight - treeHeight - 1)
        {
            level.Writer.SetBlockWithoutNotifyingNeighbors(x, y - 1, z, level.Content.Blocks.Get("dirt").Id, 0, false);


            for (var leafY = y - 3 + treeHeight; leafY <= y + treeHeight; ++leafY)
            {
                var relativeY = leafY - (y + treeHeight);
                var leafRadius = 1 - relativeY / 2;

                for (var leafX = x - leafRadius; leafX <= x + leafRadius; ++leafX)
                {
                    var offsetX = leafX - x;
                    for (var leafZ = z - leafRadius; leafZ <= z + leafRadius; ++leafZ)
                    {
                        var offsetZ = leafZ - z;
                        var isCorner = (Math.Abs(offsetX) != leafRadius ||
                                        Math.Abs(offsetZ) != leafRadius ||
                                        (rand.NextInt(2) != 0 && relativeY != 0)) && !level.Content.Blocks.IsOpaque(level.Reader.GetBlockId(leafX, leafY, leafZ));
                        if (isCorner)
                        {
                            level.Writer.SetBlockWithoutNotifyingNeighbors(leafX, leafY, leafZ, level.Content.Blocks.Get("leaves").Id, 2, false);
                        }
                    }
                }
            }

            for (var trunkY = 0; trunkY < treeHeight; ++trunkY)
            {
                var blockAtTrunk = level.Reader.GetBlockId(x, y + trunkY, z);
                if (blockAtTrunk == 0 || blockAtTrunk == level.Content.Blocks.Get("leaves").Id)
                {
                    level.Writer.SetBlockWithoutNotifyingNeighbors(x, y + trunkY, z, level.Content.Blocks.Get("log").Id, 2, false);
                }
            }

            return true;
        }

        return false;
    }
}
