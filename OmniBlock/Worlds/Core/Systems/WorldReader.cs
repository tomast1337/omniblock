using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Biomes.Source;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Dimensions;

namespace OmniBlock.Worlds.Core.Systems;

public class WorldReader : IBlockReader
{
    private readonly IWorldContext _context;
    private readonly Dimension _dimension;

    public WorldReader(IWorldContext context, Dimension dimension)
    {
        _dimension = dimension;
        _context = context;
    }

    public int AmbientDarkness => _context.Environment?.AmbientDarkness ?? 0;

    public bool IsPosLoaded(int x, int y, int z) => y >= 0 && y < ChuckFormat.WorldHeight && _context.ChunkHost.HasChunk(x >> 4, z >> 4);

    public int GetBlockId(int x, int y, int z)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000 || y < 0 || y >= ChuckFormat.WorldHeight)
        {
            return 0;
        }

        return _context.ChunkHost.GetChunk(x >> 4, z >> 4).GetBlockId(x & 15, y, z & 15);
    }

    public int GetBlockMeta(int x, int y, int z)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000 || y < 0 || y >= ChuckFormat.WorldHeight)
        {
            return 0;
        }

        return _context.ChunkHost.GetChunk(x >> 4, z >> 4).GetBlockMeta(x & 15, y, z & 15);
    }

    public Material GetMaterial(int x, int y, int z)
    {
        int blockId = GetBlockId(x, y, z);
        return blockId == 0 ? Material.Air : _context.Content.Blocks.GetByProtocolId(blockId).Material;
    }

    public bool IsOpaque(int x, int y, int z)
    {
        return _context.Content.Blocks.TryGetByProtocolId(GetBlockId(x, y, z), out Block? block) && block.IsOpaque;
    }

    public bool ShouldSuffocate(int x, int y, int z)
    {
        if (!IsPosLoaded(x, y, z))
        {
            return false;
        }

        return _context.Content.Blocks.TryGetByProtocolId(GetBlockId(x, y, z), out Block? block)
               && block.Material.Suffocates && block.IsFullCube();
    }

    public BiomeSource GetBiomeSource() => _dimension.BiomeSource;

    public bool IsAir(int x, int y, int z) => GetBlockId(x, y, z) == 0;

    public int GetBrightness(int x, int y, int z)
    {
        if (y < 0)
        {
            return 0;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return !_dimension.HasCeiling ? 15 : 0;
        }

        return _context.ChunkHost.GetChunk(x >> 4, z >> 4).GetLight(x & 15, y, z & 15, 0);
    }

    public bool IsTopY(int x, int y, int z)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000) return false;

        if (y < 0)
        {
            return false;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return true;
        }

        if (!_context.ChunkHost.HasChunk(x >> 4, z >> 4))
        {
            return false;
        }

        Chunk chunk = _context.ChunkHost.GetChunk(x >> 4, z >> 4);
        return chunk.IsAboveMaxHeight(x & 15, y, z & 15);
    }

    public int GetTopY(int x, int z)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000) return 0;

        int chunkX = x >> 4;
        int chunkZ = z >> 4;

        if (!_context.ChunkHost.HasChunk(chunkX, chunkZ))
        {
            return 0;
        }

        Chunk chunk = _context.ChunkHost.GetChunk(chunkX, chunkZ);
        return chunk.GetHeight(x & 15, z & 15);

    }

    public int GetTopSolidBlockY(int x, int z)
    {
        Chunk chunk = _context.ChunkHost.GetChunkFromPos(x, z);
        int currentY = ChuckFormat.WorldHeight - 1;
        int localX = x & 15;
        int localZ = z & 15;

        for (; currentY > 0; --currentY)
        {
            int blockId = chunk.GetBlockId(localX, currentY, localZ);
            Material material = blockId == 0 ? Material.Air : _context.Content.Blocks.GetByProtocolId(blockId).Material;

            if (material.BlocksMovement || material.IsFluid)
            {
                return currentY + 1;
            }
        }

        return -1;
    }

    public int GetSpawnPositionValidityY(int x, int z)
    {
        Chunk chunk = _context.ChunkHost.GetChunkFromPos(x, z);
        int currentY = ChuckFormat.WorldHeight - 1;
        int localX = x & 15;
        int localZ = z & 15;

        for (; currentY > 0; currentY--)
        {
            int blockId = chunk.GetBlockId(localX, currentY, localZ);
            if (blockId != 0 && _context.Content.Blocks.GetByProtocolId(blockId).Material.BlocksMovement)
            {
                return currentY + 1;
            }
        }

        return -1;
    }

    public HitResult Raycast(Vec3D start, Vec3D end, bool includeFluids = false, bool ignoreNonSolid = false) =>
        BlockRaycaster.Cast(this, _context.Entities, _context.Content.Blocks, start, end, includeFluids, ignoreNonSolid);

    public float GetVisibilityRatio(Vec3D sourcePosition, Box targetBox)
    {
        double stepSizeX = 1.0D / ((targetBox.MaxX - targetBox.MinX) * 2.0D + 1.0D);
        double stepSizeY = 1.0D / ((targetBox.MaxY - targetBox.MinY) * 2.0D + 1.0D);
        double stepSizeZ = 1.0D / ((targetBox.MaxZ - targetBox.MinZ) * 2.0D + 1.0D);

        int visiblePoints = 0;
        int totalPoints = 0;

        for (float progressX = 0.0F; progressX <= 1.0F; progressX = (float)(progressX + stepSizeX))
        {
            for (float progressY = 0.0F; progressY <= 1.0F; progressY = (float)(progressY + stepSizeY))
            {
                for (float progressZ = 0.0F; progressZ <= 1.0F; progressZ = (float)(progressZ + stepSizeZ))
                {
                    double sampleX = targetBox.MinX + (targetBox.MaxX - targetBox.MinX) * progressX;
                    double sampleY = targetBox.MinY + (targetBox.MaxY - targetBox.MinY) * progressY;
                    double sampleZ = targetBox.MinZ + (targetBox.MaxZ - targetBox.MinZ) * progressZ;
                    if (Raycast(new Vec3D(sampleX, sampleY, sampleZ), sourcePosition).Type == HitResultType.Miss)
                    {
                        visiblePoints++;
                    }

                    totalPoints++;
                }
            }
        }

        return (float)visiblePoints / totalPoints;
    }

    public bool IsMaterialInBox(Box area, Func<Material, bool> predicate)
    {
        int minX = MathHelper.Floor(area.MinX);
        int maxX = MathHelper.Floor(area.MaxX + 1.0D);
        int minY = MathHelper.Floor(area.MinY);
        int maxY = MathHelper.Floor(area.MaxY + 1.0D);
        int minZ = MathHelper.Floor(area.MinZ);
        int maxZ = MathHelper.Floor(area.MaxZ + 1.0D);

        if (area.MinX < 0.0D)
        {
            minX--;
        }

        if (area.MinY < 0.0D)
        {
            minY--;
        }

        if (area.MinZ < 0.0D)
        {
            minZ--;
        }

        for (int x = minX; x < maxX; ++x)
        {
            for (int y = minY; y < maxY; ++y)
            {
                for (int z = minZ; z < maxZ; ++z)
                {
                    if (predicate(GetMaterial(x, y, z)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool UpdateMovementInFluid(Box entityBox, Material fluidMaterial, Entity entity)
    {
        int minX = MathHelper.Floor(entityBox.MinX);
        int maxX = MathHelper.Floor(entityBox.MaxX + 1.0D);
        int minY = MathHelper.Floor(entityBox.MinY);
        int maxY = MathHelper.Floor(entityBox.MaxY + 1.0D);
        int minZ = MathHelper.Floor(entityBox.MinZ);
        int maxZ = MathHelper.Floor(entityBox.MaxZ + 1.0D);

        if (!_context.ChunkHost.IsRegionLoaded(minX, minY, minZ, maxX, maxY, maxZ))
        {
            return false;
        }

        bool isSubmerged = false;
        Vec3D flowVector = new(0.0D, 0.0D, 0.0D);

        for (int x = minX; x < maxX; ++x)
        {
            for (int y = minY; y < maxY; ++y)
            {
                for (int z = minZ; z < maxZ; ++z)
                {
                    if (_context.Content.Blocks.TryGetByProtocolId(GetBlockId(x, y, z), out Block? block)
                        && block.Material == fluidMaterial)
                    {
                        double fluidSurfaceY = y + 1 - FluidMath.GetFluidHeightFromMeta(GetBlockMeta(x, y, z));

                        if (maxY >= fluidSurfaceY)
                        {
                            isSubmerged = true;
                            Vec3D blockFlow = block.ApplyVelocity(new OnApplyVelocityEvent(_context, entity, x, y, z));
                            flowVector.X += blockFlow.X;
                            flowVector.Y += blockFlow.Y;
                            flowVector.Z += blockFlow.Z;
                        }
                    }
                }
            }
        }

        if (flowVector.Magnitude() > 0.0D)
        {
            flowVector = flowVector.Normalize();
            const double flowStrength = 0.014D;
            entity.VelocityX += flowVector.X * flowStrength;
            entity.VelocityY += flowVector.Y * flowStrength;
            entity.VelocityZ += flowVector.Z * flowStrength;
        }

        return isSubmerged;
    }

    public void MarkChunkDirty(int x, int z)
    {
        if (_context.Reader.IsPosLoaded(x, 0, z))
        {
            _context.ChunkHost.GetChunkFromPos(x, z).MarkDirty();
        }
    }
}
