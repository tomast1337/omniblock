using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Storage.RegionFormat;

public interface IChunkStorage
{
    /// <summary>
    ///     Probes authoritative storage without decoding or constructing chunk entities. Background
    ///     generation uses this to ensure an existing/player-edited chunk is never treated as an
    ///     empty generation target. Implementations without a probe conservatively report false.
    /// </summary>
    bool ContainsChunk(int chunkX, int chunkZ) => false;

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
