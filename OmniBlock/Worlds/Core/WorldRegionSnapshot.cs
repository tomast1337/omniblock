using System.Buffers;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Biomes.Source;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Core;

/// <summary>
///     A read-only copy of exactly the block/light cells inside [minX,maxX]x[minY,maxY]x[minZ,maxZ]
///     (inclusive), not whole chunk columns — safe to read from a worker thread while the live
///     chunks it was built from keep changing. Chunk mesh building is the only caller today and
///     requests a 16-block sub-chunk plus 1 block of padding on every side (18x18x18) for face
///     culling and AO, which is cheap enough to copy synchronously on the calling thread before
///     handing the actual mesh build off to a worker. Terrain LOD compilation also uses a full
///     16x128x16 column snapshot so biome tint and metadata-sensitive bounds remain worker-safe.
/// </summary>
public class WorldRegionSnapshot : IBlockReader, ILightProvider, IDisposable
{
    private readonly BiomeSource _biomeSource;
    private readonly byte[] _blockLight;

    private readonly byte[] _blocks;
    private readonly float[] _lightTable;
    private readonly byte[] _meta;

    private readonly int _minX;
    private readonly int _minY;
    private readonly int _minZ;
    private readonly int _sizeX;
    private readonly int _sizeY;
    private readonly int _sizeZ;
    private readonly byte[] _skyLight;
    private readonly int _skylightSubtracted;

    public WorldRegionSnapshot(IWorldContext world, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        ContentBlocks = world.Content.Blocks;
        _biomeSource = world.Dimension.BiomeSource.Clone();

        _minX = minX;
        _minY = minY;
        _minZ = minZ;
        _sizeX = maxX - minX + 1;
        _sizeY = maxY - minY + 1;
        _sizeZ = maxZ - minZ + 1;

        var cellCount = _sizeX * _sizeY * _sizeZ;
        var nibbleByteCount = (cellCount + 1) >> 1;

        _blocks = ArrayPool<byte>.Shared.Rent(cellCount);
        _meta = ArrayPool<byte>.Shared.Rent(nibbleByteCount);
        _skyLight = ArrayPool<byte>.Shared.Rent(nibbleByteCount);
        _blockLight = ArrayPool<byte>.Shared.Rent(nibbleByteCount);

        // Cells outside [0, WorldHeight) are never read (GetBlockId/GetLightValueExt guard on Y
        // before touching the arrays above), so the padding rows above/below the world don't
        // need to be written here.
        var rowMinY = Math.Max(minY, 0);
        var rowMaxY = Math.Min(maxY, ChuckFormat.WorldHeight - 1);

        var minChunkX = minX >> 4;
        var maxChunkX = maxX >> 4;
        var minChunkZ = minZ >> 4;
        var maxChunkZ = maxZ >> 4;

        for (var cx = minChunkX; cx <= maxChunkX; cx++)
        {
            var columnMinX = Math.Max(minX, cx << 4);
            var columnMaxX = Math.Min(maxX, (cx << 4) + 15);

            for (var cz = minChunkZ; cz <= maxChunkZ; cz++)
            {
                var columnMinZ = Math.Max(minZ, cz << 4);
                var columnMaxZ = Math.Min(maxZ, (cz << 4) + 15);

                var chunk = world.ChunkHost.GetChunk(cx, cz);

                for (var worldX = columnMinX; worldX <= columnMaxX; worldX++)
                {
                    var chunkLocalX = worldX & 15;

                    for (var worldZ = columnMinZ; worldZ <= columnMaxZ; worldZ++)
                    {
                        var chunkLocalZ = worldZ & 15;

                        for (var worldY = rowMinY; worldY <= rowMaxY; worldY++)
                        {
                            var index = LocalIndex(worldX - minX, worldY - minY, worldZ - minZ);
                            _blocks[index] = chunk.Blocks[ChuckFormat.GetIndex(chunkLocalX, worldY, chunkLocalZ)];
                            SetNibble(_meta, index, chunk.Meta.GetNibble(chunkLocalX, worldY, chunkLocalZ));
                            SetNibble(_skyLight, index, chunk.SkyLight.GetNibble(chunkLocalX, worldY, chunkLocalZ));
                            SetNibble(_blockLight, index, chunk.BlockLight.GetNibble(chunkLocalX, worldY, chunkLocalZ));
                        }
                    }
                }
            }
        }

        _lightTable = world.Dimension.LightLevelToLuminance;
        _skylightSubtracted = world.Environment.AmbientDarkness;
    }

    public IBlockRuntimeView ContentBlocks { get; }

    public bool IsLit { get; private set; }

    public int GetBlockId(int x, int y, int z)
    {
        if (y < 0 || y >= ChuckFormat.WorldHeight)
        {
            return 0;
        }

        return TryGetLocalIndex(x, y, z, out var index) ? _blocks[index] : 0;
    }

    public BiomeSource GetBiomeSource() => _biomeSource;

    public bool ShouldSuffocate(int x, int y, int z)
    {
        return ContentBlocks.TryGetByProtocolId(GetBlockId(x, y, z), out var block)
               && block.Material.BlocksMovement && block.IsFullCube();
    }

    public bool IsOpaque(int x, int y, int z) => ContentBlocks.TryGetByProtocolId(GetBlockId(x, y, z), out var block) && block.IsOpaque;

    public int GetBlockMeta(int x, int y, int z)
    {
        if (y < 0 || y >= ChuckFormat.WorldHeight)
        {
            return 0;
        }

        return TryGetLocalIndex(x, y, z, out var index) ? GetNibble(_meta, index) : 0;
    }

    public Material GetMaterial(int x, int y, int z)
    {
        var blockId = GetBlockId(x, y, z);
        return blockId == 0 ? Material.Air : ContentBlocks.GetByProtocolId(blockId).Material;
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

    public void MarkChunkDirty(int x, int z) => throw new NotImplementedException();

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        ArrayPool<byte>.Shared.Return(_blocks);
        ArrayPool<byte>.Shared.Return(_meta);
        ArrayPool<byte>.Shared.Return(_skyLight);
        ArrayPool<byte>.Shared.Return(_blockLight);
    }

    public float GetNaturalBrightness(int x, int y, int z, int minLight)
    {
        var light = GetLightValue(x, y, z);
        return _lightTable[Math.Max(light, minLight)];
    }

    public float GetLuminance(int x, int y, int z) => _lightTable[GetLightValue(x, y, z)];

    public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
        GetLightLevelsExt(x, y, z, true).WithBlockFloor(minBlockLight);

    private int LocalIndex(int lx, int ly, int lz) => (lx * _sizeZ + lz) * _sizeY + ly;

    private bool TryGetLocalIndex(int x, int y, int z, out int index)
    {
        var lx = x - _minX;
        var ly = y - _minY;
        var lz = z - _minZ;

        if ((uint)lx >= (uint)_sizeX || (uint)ly >= (uint)_sizeY || (uint)lz >= (uint)_sizeZ)
        {
            index = -1;
            return false;
        }

        index = LocalIndex(lx, ly, lz);
        return true;
    }

    private static int GetNibble(byte[] nibbles, int index) =>
        (index & 1) == 0 ? nibbles[index >> 1] & 0x0F : (nibbles[index >> 1] >> 4) & 0x0F;

    private static void SetNibble(byte[] nibbles, int index, int value)
    {
        var byteIndex = index >> 1;
        nibbles[byteIndex] = (index & 1) == 0
            ? (byte)((nibbles[byteIndex] & 0xF0) | (value & 0x0F))
            : (byte)((nibbles[byteIndex] & 0x0F) | ((value & 0x0F) << 4));
    }

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
            var blockId = GetBlockId(x, y, z);
            if (blockId == ContentBlocks.Get("slab").Id || blockId == ContentBlocks.Get("farmland").Id || blockId == ContentBlocks.Get("wooden_stairs").Id || blockId == ContentBlocks.Get("cobblestone_stairs").Id)
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

        // Outside the snapshot's bounded volume: only reachable via the stairs-check probe
        // right at the edge of the padding. No data to be more precise with, so this reads the
        // same as "above the world": full sun, no torch.
        if (!TryGetLocalIndex(x, y, z, out var index))
        {
            return LightLevels.FullSky;
        }

        var skyLight = GetNibble(_skyLight, index);
        if (skyLight > 0)
        {
            IsLit = true;
        }

        return LightLevels.Of(skyLight, GetNibble(_blockLight, index));
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
            var blockId = GetBlockId(x, y, z);
            if (blockId == ContentBlocks.Get("slab").Id || blockId == ContentBlocks.Get("farmland").Id || blockId == ContentBlocks.Get("wooden_stairs").Id || blockId == ContentBlocks.Get("cobblestone_stairs").Id)
            {
                var maxLight = GetLightValueExt(x, y + 1, z, false);
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

        // Outside the snapshot's bounded volume — see GetLightLevelsExt.
        if (!TryGetLocalIndex(x, y, z, out var index))
        {
            return Math.Max(0, 15 - _skylightSubtracted);
        }

        var skyLight = GetNibble(_skyLight, index);
        if (skyLight > 0)
        {
            IsLit = true;
        }

        skyLight -= _skylightSubtracted;
        var blockLight = GetNibble(_blockLight, index);
        if (blockLight > skyLight)
        {
            skyLight = blockLight;
        }

        return skyLight;
    }

    ~WorldRegionSnapshot() => Dispose();
}
