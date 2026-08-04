using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Biomes.Source;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Worlds.Core;

public class WorldRegionSnapshot : IBlockReader, ILightProvider, IDisposable
{
    public bool IsLit { get; private set; }

    private readonly BiomeSource _biomeSource;
    private readonly ChunkSnapshot[,] _chunks;
    private readonly int _chunkX;
    private readonly int _chunkZ;
    private readonly float[] _lightTable;
    private readonly int _skylightSubtracted;

    public WorldRegionSnapshot(IWorldContext world, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        _biomeSource = world.Dimension.BiomeSource.Clone();

        _chunkX = minX >> 4;
        _chunkZ = minZ >> 4;
        int maxChunkX = maxX >> 4;
        int maxChunkZ = maxZ >> 4;

        int width = maxChunkX - _chunkX + 1;
        int depth = maxChunkZ - _chunkZ + 1;

        _chunks = new ChunkSnapshot[width, depth];

        for (int cx = _chunkX; cx <= maxChunkX; ++cx)
        {
            for (int cz = _chunkZ; cz <= maxChunkZ; ++cz)
            {
                Chunk originalChunk = world.ChunkHost.GetChunk(cx, cz);
                _chunks[cx - _chunkX, cz - _chunkZ] = new ChunkSnapshot(originalChunk);
            }
        }

        _lightTable = world.Dimension.LightLevelToLuminance;
        _skylightSubtracted = world.Environment.AmbientDarkness;
    }

    public int GetBlockId(int x, int y, int z)
    {
        if (y < 0 || y >= ChuckFormat.WorldHeight)
        {
            return 0;
        }

        int chunkIdxX = (x >> 4) - _chunkX;
        int chunkIdxZ = (z >> 4) - _chunkZ;

        if (chunkIdxX >= 0 && chunkIdxX < _chunks.GetLength(0) &&
            chunkIdxZ >= 0 && chunkIdxZ < _chunks.GetLength(1))
        {
            return _chunks[chunkIdxX, chunkIdxZ].GetBlockID(x & 15, y, z & 15);
        }

        return 0;
    }

    public BiomeSource GetBiomeSource() => _biomeSource;

    public bool ShouldSuffocate(int x, int y, int z)
    {
        Block block = Block.Blocks[GetBlockId(x, y, z)];
        return block != null && block.Material.BlocksMovement && block.IsFullCube();
    }

    public bool IsOpaque(int x, int y, int z)
    {
        Block block = Block.Blocks[GetBlockId(x, y, z)];
        return block != null && block.IsOpaque;
    }

    public int GetBlockMeta(int x, int y, int z)
    {
        if (y < 0 || y >= ChuckFormat.WorldHeight)
        {
            return 0;
        }

        int chunkIdxX = (x >> 4) - _chunkX;
        int chunkIdxZ = (z >> 4) - _chunkZ;
        return _chunks[chunkIdxX, chunkIdxZ].GetBlockMetadata(x & 15, y, z & 15);
    }

    public Material GetMaterial(int x, int y, int z)
    {
        int blockId = GetBlockId(x, y, z);
        return blockId == 0 ? Material.Air : Block.Blocks[blockId].Material;
    }

    public bool IsAir(int x, int y, int z) => GetBlockId(x, y, z) == 0;
    public int GetBrightness(int x, int y, int z) => GetLightValue(x, y, z);
    public bool IsTopY(int x, int y, int z) => throw new NotImplementedException();
    public int GetTopY(int x, int z) => throw new NotImplementedException();
    public int GetTopSolidBlockY(int x, int z) => throw new NotImplementedException();
    public int GetSpawnPositionValidityY(int x, int z) => throw new NotImplementedException();
    public float GetVisibilityRatio(Vec3D sourcePosition, Box targetBox) => throw new NotImplementedException();
    public HitResult Raycast(Vec3D start, Vec3D end, bool includeFluids = false, bool ignoreNonSolid = false) => throw new NotImplementedException();
    public bool IsMaterialInBox(Box area, Func<Material, bool> predicate) => throw new NotImplementedException();
    public bool UpdateMovementInFluid(Box entityBox, Material fluidMaterial, Entity entity) => throw new NotImplementedException();
    public bool IsPosLoaded(int x, int y, int z) => throw new NotImplementedException();

    public float GetNaturalBrightness(int x, int y, int z, int minLight)
    {
        int light = GetLightValue(x, y, z);
        return _lightTable[Math.Max(light, minLight)];
    }

    public float GetLuminance(int x, int y, int z) => _lightTable[GetLightValue(x, y, z)];

    public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
        GetLightLevelsExt(x, y, z, true).WithBlockFloor(minBlockLight);

    /// <summary>
    ///     <see cref="GetLightValueExt" /> per channel, with the time of day left to the shader.
    /// </summary>
    /// <remarks>
    ///     The slab and stairs case takes the brightest neighbour in each channel separately, where
    ///     the collapsed form took the brightest collapsed neighbour. Those disagree whenever the
    ///     sunniest neighbour and the best-lit one are not the same cell, and there is no reading
    ///     that keeps both — the collapsed answer cannot be taken apart again.
    /// </remarks>
    public LightLevels GetLightLevelsExt(int x, int y, int z, bool checkStairs)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000)
        {
            return LightLevels.FullSky;
        }

        if (checkStairs)
        {
            int blockId = GetBlockId(x, y, z);
            if (blockId == BlockRegistry.Get("slab").Id || blockId == BlockRegistry.Get("farmland").Id || blockId == BlockRegistry.Get("wooden_stairs").Id || blockId == BlockRegistry.Get("cobblestone_stairs").Id)
            {
                return GetLightLevelsExt(x, y + 1, z, false)
                    .Max(GetLightLevelsExt(x + 1, y, z, false))
                    .Max(GetLightLevelsExt(x - 1, y, z, false))
                    .Max(GetLightLevelsExt(x, y, z + 1, false))
                    .Max(GetLightLevelsExt(x, y, z - 1, false));
            }
        }

        if (y < 0)
        {
            return default;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return LightLevels.FullSky;
        }

        ref ChunkSnapshot chunk = ref _chunks[(x >> 4) - _chunkX, (z >> 4) - _chunkZ];
        LightLevels levels = chunk.GetLightLevels(x & 15, y, z & 15);

        if (chunk.IsLit)
        {
            IsLit = true;
        }

        return levels;
    }

    public int GetLightValue(int x, int y, int z) => GetLightValueExt(x, y, z, true);

    public int GetLightValueExt(int x, int y, int z, bool checkStairs)
    {
        // World bounds check
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000)
        {
            return 15;
        }

        if (checkStairs)
        {
            int blockId = GetBlockId(x, y, z);
            if (blockId == BlockRegistry.Get("slab").Id || blockId == BlockRegistry.Get("farmland").Id || blockId == BlockRegistry.Get("wooden_stairs").Id || blockId == BlockRegistry.Get("cobblestone_stairs").Id)
            {
                int maxLight = GetLightValueExt(x, y + 1, z, false);
                maxLight = Math.Max(maxLight, GetLightValueExt(x + 1, y, z, false)); // East
                maxLight = Math.Max(maxLight, GetLightValueExt(x - 1, y, z, false)); // West
                maxLight = Math.Max(maxLight, GetLightValueExt(x, y, z + 1, false)); // South
                maxLight = Math.Max(maxLight, GetLightValueExt(x, y, z - 1, false)); // North
                return maxLight;
            }
        }

        if (y < 0)
        {
            return 0;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return Math.Max(0, 15 - _skylightSubtracted);
        }

        int chunkIdxX = (x >> 4) - _chunkX;
        int chunkIdxZ = (z >> 4) - _chunkZ;

        ref ChunkSnapshot chunk = ref _chunks[chunkIdxX, chunkIdxZ];

        int lightValue = chunk.GetBlockLightValue(x & 15, y, z & 15, _skylightSubtracted);

        if (chunk.IsLit)
        {
            IsLit = true;
        }

        return lightValue;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (ChunkSnapshot snapshot in _chunks)
        {
            snapshot.Dispose();
        }
    }

    ~WorldRegionSnapshot()
    {
        Dispose();
    }

    public void MarkChunkDirty(int x, int z) => throw new NotImplementedException();
}
