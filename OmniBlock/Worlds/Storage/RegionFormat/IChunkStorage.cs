using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Storage.RegionFormat;

public interface IChunkStorage
{
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
