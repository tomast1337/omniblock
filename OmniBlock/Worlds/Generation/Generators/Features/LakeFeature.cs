using OmniBlock.Blocks.Materials;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class LakeFeature : Feature
{
    private readonly int _waterBlockId;

    public LakeFeature(int waterBlockId) => _waterBlockId = waterBlockId;

    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        x -= 8;

        while (y > 0 && level.Reader.IsAir(x, y, z))
        {
            y--;
        }

        y -= 4;
        var lakeMask = new bool[2048];
        var blobCount = rand.NextInt(4) + 4;


        for (var i = 0; i < blobCount; ++i)
        {
            var radiusH = rand.NextDouble() * 6.0D + 3.0D;
            var radiusV = rand.NextDouble() * 4.0D + 2.0D;
            var radiusH2 = rand.NextDouble() * 6.0D + 3.0D;

            var centerX = rand.NextDouble() * (16.0D - radiusH - 2.0D) + 1.0D + radiusH / 2.0D;
            var centerY = rand.NextDouble() * (8.0D - radiusV - 4.0D) + 2.0D + radiusV / 2.0D;
            var centerZ = rand.NextDouble() * (16.0D - radiusH2 - 2.0D) + 1.0D + radiusH2 / 2.0D;

            for (var dx = 1; dx < 15; ++dx)
            {
                for (var dy = 1; dy < 15; ++dy)
                {
                    for (var dz = 1; dz < 7; ++dz)
                    {
                        var normX = (dx - centerX) / (radiusH / 2.0D);
                        var normY = (dz - centerY) / (radiusV / 2.0D);
                        var normZ = (dy - centerZ) / (radiusH2 / 2.0D);

                        var distSq = normX * normX + normY * normY + normZ * normZ;
                        if (distSq < 1.0D)
                        {
                            lakeMask[(dx * 16 + dy) * 8 + dz] = true;
                        }
                    }
                }
            }
        }


        for (var dx = 0; dx < 16; ++dx)
        {
            for (var dz = 0; dz < 16; ++dz)
            {
                for (var dy = 0; dy < 8; ++dy)
                {
                    var isEdge = !lakeMask[(dx * 16 + dz) * 8 + dy] && ((dx < 15 && lakeMask[((dx + 1) * 16 + dz) * 8 + dy]) || (dx > 0 && lakeMask[((dx - 1) * 16 + dz) * 8 + dy]) || (dz < 15 && lakeMask[(dx * 16 + dz + 1) * 8 + dy]) ||
                                                                        (dz > 0 && lakeMask[(dx * 16 + (dz - 1)) * 8 + dy]) || (dy < 7 && lakeMask[(dx * 16 + dz) * 8 + dy + 1]) || (dy > 0 && lakeMask[(dx * 16 + dz) * 8 + (dy - 1)]));
                    if (isEdge)
                    {
                        var mat = level.Reader.GetMaterial(x + dx, y + dy, z + dz);
                        if (dy >= 4 && mat.IsFluid)
                        {
                            return false;
                        }

                        if (dy < 4 && !mat.IsSolid && level.Reader.GetBlockId(x + dx, y + dy, z + dz) != _waterBlockId)
                        {
                            return false;
                        }
                    }
                }
            }
        }

        for (var dx = 0; dx < 16; ++dx)
        {
            for (var dy = 0; dy < 16; ++dy)
            {
                for (var dz = 0; dz < 8; ++dz)
                {
                    if (lakeMask[(dx * 16 + dy) * 8 + dz])
                    {
                        var blockId = dz >= 4 ? 0 : _waterBlockId;
                        level.Writer.SetBlockWithoutNotifyingNeighbors(x + dx, y + dz, z + dy, blockId, 0, false);
                    }
                }
            }
        }

        for (var dx = 0; dx < 16; ++dx)
        {
            for (var dy = 0; dy < 16; ++dy)
            {
                for (var dz = 4; dz < 8; ++dz)
                {
                    if (lakeMask[(dx * 16 + dy) * 8 + dz] &&
                        level.Reader.GetBlockId(x + dx, y + dz - 1, z + dy) == level.Content.Blocks.Get("dirt").Id &&
                        level.Lighting.GetBrightness(LightType.Sky, x + dx, y + dz, z + dy) > 0)
                    {
                        level.Writer.SetBlockWithoutNotifyingNeighbors(x + dx, y + dz - 1, z + dy, level.Content.Blocks.Get("grass_block").Id, 0, false);
                    }
                }
            }
        }

        if (level.Content.Blocks.GetByProtocolId(_waterBlockId).Material == Material.Lava)
        {
            for (var dx = 0; dx < 16; ++dx)
            {
                for (var dy = 0; dy < 16; ++dy)
                {
                    for (var dz = 0; dz < 8; ++dz)
                    {
                        var isEdge = !lakeMask[(dx * 16 + dy) * 8 + dz] &&
                                     (
                                         (dx < 15 && lakeMask[((dx + 1) * 16 + dy) * 8 + dz]) ||
                                         (dx > 0 && lakeMask[((dx - 1) * 16 + dy) * 8 + dz]) ||
                                         (dy < 15 && lakeMask[(dx * 16 + dy + 1) * 8 + dz]) ||
                                         (dy > 0 && lakeMask[(dx * 16 + (dy - 1)) * 8 + dz]) ||
                                         (dz < 7 && lakeMask[(dx * 16 + dy) * 8 + dz + 1]) ||
                                         (dz > 0 && lakeMask[(dx * 16 + dy) * 8 + (dz - 1)])
                                     );
                        if (isEdge && (dz < 4 || rand.NextInt(2) != 0) && level.Reader.GetMaterial(x + dx, y + dz, z + dy).IsSolid)
                        {
                            level.Writer.SetBlockWithoutNotifyingNeighbors(x + dx, y + dz, z + dy, level.Content.Blocks.Get("stone").Id, 0, false);
                        }
                    }
                }
            }
        }

        return true;
    }
}
