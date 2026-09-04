using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Generation.Generators.Features;

internal class OreFeature : Feature
{
    private readonly int _minableBlockId;
    private readonly int _numberOfBlocks;

    public OreFeature(int minableBlockId, int numberOfBlocks)
    {
        _minableBlockId = minableBlockId;
        _numberOfBlocks = numberOfBlocks;
    }

    public override bool Generate(IWorldContext ctx, JavaRandom rand, int x, int y, int z)
    {
        var angle = rand.NextFloat() * (float)Math.PI;
        var spread = _numberOfBlocks / 8.0;

        var startX = x + 8 + MathHelper.Sin(angle) * spread;
        var endX = x + 8 - MathHelper.Sin(angle) * spread;
        var startZ = z + 8 + MathHelper.Cos(angle) * spread;
        var endZ = z + 8 - MathHelper.Cos(angle) * spread;

        double startY = y + rand.NextInt(3) + 2;
        double endY = y + rand.NextInt(3) + 2;

        for (var i = 0; i <= _numberOfBlocks; ++i)
        {
            var centerX = startX + (endX - startX) * i / _numberOfBlocks;
            var centerY = startY + (endY - startY) * i / _numberOfBlocks;
            var centerZ = startZ + (endZ - startZ) * i / _numberOfBlocks;

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
                var dx = (blockX + 0.5 - centerX) / (radiusH / 2.0);
                if (dx * dx >= 1.0)
                {
                    continue;
                }

                for (var blockY = minY; blockY <= maxY; ++blockY)
                {
                    var dy = (blockY + 0.5 - centerY) / (radiusV / 2.0);
                    if (dx * dx + dy * dy >= 1.0)
                    {
                        continue;
                    }

                    for (var blockZ = minZ; blockZ <= maxZ; ++blockZ)
                    {
                        var dz = (blockZ + 0.5 - centerZ) / (radiusH / 2.0);

                        if (dx * dx + dy * dy + dz * dz < 1.0 && ctx.Reader.GetBlockId(blockX, blockY, blockZ) == ctx.Content.Blocks.Get("stone").Id)
                        {
                            ctx.Writer.SetBlockWithoutNotifyingNeighbors(blockX, blockY, blockZ, _minableBlockId, 0, false);
                        }
                    }
                }
            }
        }

        return true;
    }
}
