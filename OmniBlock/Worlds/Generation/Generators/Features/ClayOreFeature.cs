using OmniBlock.Blocks.Materials;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class ClayOreFeature : Feature
{
    private readonly int _numberOfBlocks;

    public ClayOreFeature(int numberOfBlocks) => _numberOfBlocks = numberOfBlocks;

    public override bool Generate(IWorldContext level, JavaRandom rand, int x, int y, int z)
    {
        if (level.Reader.GetMaterial(x, y, z) != Material.Water)
        {
            return false;
        }

        var angle = rand.NextFloat() * (float)Math.PI;
        var spread = _numberOfBlocks / 8.0;

        var startX = x + 8 + MathHelper.Sin(angle) * spread;
        var endX = x + 8 - MathHelper.Sin(angle) * spread;
        var startZ = z + 8 + MathHelper.Cos(angle) * spread;
        var enZ = z + 8 - MathHelper.Cos(angle) * spread;

        double startY = y + rand.NextInt(3) + 2;
        double endY = y + rand.NextInt(3) + 2;

        for (var i = 0; i <= _numberOfBlocks; ++i)
        {
            float lerp = i * _numberOfBlocks;
            var centerX = startX + (endX - startX) * lerp;
            var centerY = startY + (endY - startY) * lerp;
            var centerZ = startZ + (enZ - startZ) * lerp;

            var sizeMultiplier = rand.NextDouble() * _numberOfBlocks / 16.0D;
            var radiusH = (MathHelper.Sin(i * (float)Math.PI / _numberOfBlocks) + 1.0F) * sizeMultiplier + 1.0D;
            var radiusV = (MathHelper.Sin(i * (float)Math.PI / _numberOfBlocks) + 1.0F) * sizeMultiplier + 1.0D;

            var minX = MathHelper.Floor(centerX - radiusH / 2.0D);
            var minY = MathHelper.Floor(centerY - radiusV / 2.0D);
            var minZ = MathHelper.Floor(centerZ - radiusH / 2.0D);

            var maxX = MathHelper.Floor(centerX + radiusH / 2.0D);
            var maxY = MathHelper.Floor(centerY + radiusV / 2.0D);
            var maxZ = MathHelper.Floor(centerZ + radiusH / 2.0D);

            for (var blockX = minX; blockX <= maxX; ++blockX)
            {
                for (var blockY = minY; blockY <= maxY; ++blockY)
                {
                    for (var blockZ = minZ; blockZ <= maxZ; ++blockZ)
                    {
                        var dx = (blockX + 0.5D - centerX) / (radiusH / 2.0D);
                        var dy = (blockY + 0.5D - centerY) / (radiusV / 2.0D);
                        var dz = (blockZ + 0.5D - centerZ) / (radiusH / 2.0D);
                        if (dx * dx + dy * dy + dz * dz < 1.0D)
                        {
                            var blockId = level.Reader.GetBlockId(blockX, blockY, blockZ);
                            if (blockId == level.Content.Blocks.Get("sand").Id)
                            {
                                level.Writer.SetBlockWithoutNotifyingNeighbors(blockX, blockY, blockZ, level.Content.Blocks.Get("clay").Id, 0, false);
                            }
                        }
                    }
                }
            }
        }

        return true;
    }
}
