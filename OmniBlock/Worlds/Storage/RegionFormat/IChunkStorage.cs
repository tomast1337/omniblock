using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Worlds.Storage.RegionFormat;

public interface IChunkStorage
{
    /// <summary>
    ///     Probes authoritative storage without decoding or constructing chunk entities. Background
    ///     generation uses this to ensure an existing/player-edited chunk is never treated as an
    ///     empty generation target. Implementations without a probe conservatively report false.
    /// </summary>
    bool ContainsChunk(int chunkX, int chunkZ) => false;

    /// <summary>
    ///     Reads saved terrain for distant rendering without activating a gameplay chunk or
    ///     generating missing terrain. Unsupported stores return null.
    /// </summary>
    TerrainLodSourceSnapshot? ReadTerrainLodSource(
        int chunkX, int chunkZ, bool hasSkyLight) => null;

    Chunk? LoadChunk(IWorldContext world, int chunkX, int chunkZ);

    ChunkSaveResult SaveChunk(IWorldContext world, Chunk chunk, Action? onSave, long sequence);

    void SaveEntities(IWorldContext world, Chunk chunk);

    void Tick();

    void Flush();

    /// <summary>Completes pending writes and requests an operating-system durable flush.</summary>
    void FlushToDisk();
}

/// <summary>Confirmation that a synchronous chunk write reached its region stream.</summary>
public readonly record struct ChunkSaveResult(long SizeDeltaBytes);
