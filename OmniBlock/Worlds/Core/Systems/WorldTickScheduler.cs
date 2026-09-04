using OmniBlock.Blocks;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Worlds.Core.Systems;

public class WorldTickScheduler
{
    private readonly IWorldContext _context;
    private readonly HashSet<ScheduledBlockTick> _pendingScheduledKeys = new();

    private readonly Lock _queueLock = new();
    private readonly PriorityQueue<BlockUpdate, (long, long)> _scheduledUpdates = new();

    public WorldTickScheduler(IWorldContext context) => _context = context;

    public long Count
    {
        get
        {
            lock (_queueLock)
            {
                return _scheduledUpdates.Count;
            }
        }
    }

    public void Tick(bool forceFlush = false) => ProcessScheduledTicks(forceFlush);

    /// <summary>
    ///     Schedules a block update when restoring from chunk NBT (TileTicks). Skips the load-radius check
    ///     because Chunk.Load() runs when the chunk is being loaded—neighbor chunks for the ±8 block radius
    ///     may not be loaded yet, which would cause ticks to be silently dropped.
    /// </summary>
    public void ScheduleBlockUpdateFromChunkLoad(int x, int y, int z, int blockId, int tickRate)
    {
        var key = new ScheduledBlockTick(x, y, z, blockId);

        lock (_queueLock)
        {
            if (!_pendingScheduledKeys.Add(key))
                return;

            var scheduledTime = _context.GetTime() + tickRate;
            BlockUpdate blockUpdate = new(x, y, z, blockId, scheduledTime);
            _scheduledUpdates.Enqueue(blockUpdate, (blockUpdate.ScheduledTime, blockUpdate.ScheduledOrder));
        }
    }

    public virtual void ScheduleBlockUpdate(int x, int y, int z, int blockId, int tickRate, bool instantBlockUpdateEnabled = false)
    {
        const byte loadRadius = 8;
        var minY = Math.Max(0, y - loadRadius);
        var maxY = Math.Min(ChuckFormat.WorldHeight - 1, y + loadRadius);

        if (!_context.ChunkHost.IsPosLoaded(x - loadRadius, minY, z - loadRadius) ||
            !_context.ChunkHost.IsPosLoaded(x + loadRadius, maxY, z + loadRadius))
            return;

        if (instantBlockUpdateEnabled)
        {
            var currentBlockId = _context.Reader.GetBlockId(x, y, z);
            if (currentBlockId == blockId && currentBlockId > 0)
            {
                var meta = _context.Reader.GetBlockMeta(x, y, z);
                _context.Content.Blocks.GetByProtocolId(currentBlockId).OnTick(new OnTickEvent(_context, x, y, z, meta, currentBlockId));
            }
        }
        else
        {
            var key = new ScheduledBlockTick(x, y, z, blockId);

            lock (_queueLock)
            {
                if (!_pendingScheduledKeys.Add(key))
                    return;

                var executionTime = _context.GetTime() + tickRate;
                BlockUpdate blockUpdate = new(x, y, z, blockId, executionTime);
                _scheduledUpdates.Enqueue(blockUpdate, (blockUpdate.ScheduledTime, blockUpdate.ScheduledOrder));
            }
        }
    }

    public void ClearFullQueue()
    {
        lock (_queueLock)
        {
            _scheduledUpdates.Clear();
            _pendingScheduledKeys.Clear();
            Console.WriteLine("[Scheduler] Logic Reset: All pending keys and updates have been purged.");
        }
    }

    private void ProcessScheduledTicks(bool forceFlush)
    {
        if (_context.IsRemote) return;

        var currentTime = _context.GetTime();

        List<BlockUpdate> readyToExecute = new();
        List<BlockUpdate> deferredTicks = new();

        lock (_queueLock)
        {
            var proportionalLimit = Math.Clamp(_scheduledUpdates.Count / 10, 1000, 8192);
            var maxTicksPerFrame = forceFlush ? _scheduledUpdates.Count : proportionalLimit;
            var h = ChuckFormat.WorldHeight - 1;

            for (var i = 0; i < maxTicksPerFrame; ++i)
            {
                if (_scheduledUpdates.Count == 0) break;
                if (!forceFlush && _scheduledUpdates.Peek().ScheduledTime > currentTime) break;

                var blockUpdate = _scheduledUpdates.Dequeue();
                var key = new ScheduledBlockTick(blockUpdate.X, blockUpdate.Y, blockUpdate.Z, blockUpdate.BlockId);
                _pendingScheduledKeys.Remove(key);

                if (!_context.Reader.IsPosLoaded(blockUpdate.X, blockUpdate.Y, blockUpdate.Z))
                {
                    deferredTicks.Add(blockUpdate);
                    continue;
                }

                var currentBlockId = _context.Reader.GetBlockId(blockUpdate.X, blockUpdate.Y, blockUpdate.Z);
                if (currentBlockId != blockUpdate.BlockId || currentBlockId <= 0)
                    continue;

                const byte loadRadius = 8;
                var minY = Math.Max(0, blockUpdate.Y - loadRadius);
                var maxY = Math.Min(h, blockUpdate.Y + loadRadius);

                var posLoaded = _context.Reader.IsPosLoaded(blockUpdate.X - loadRadius, minY, blockUpdate.Z - loadRadius) &&
                                _context.Reader.IsPosLoaded(blockUpdate.X + loadRadius, maxY, blockUpdate.Z + loadRadius);

                if (!posLoaded)
                {
                    deferredTicks.Add(blockUpdate);
                    continue;
                }

                readyToExecute.Add(blockUpdate);
            }

            foreach (var def in deferredTicks)
            {
                var key = new ScheduledBlockTick(def.X, def.Y, def.Z, def.BlockId);
                _pendingScheduledKeys.Add(key);
                _scheduledUpdates.Enqueue(def, (def.ScheduledTime, def.ScheduledOrder));
            }
        }

        foreach (var blockUpdate in readyToExecute)
        {
            var meta = _context.Reader.GetBlockMeta(blockUpdate.X, blockUpdate.Y, blockUpdate.Z);
            _context.Content.Blocks.GetByProtocolId(blockUpdate.BlockId).OnTick(new OnTickEvent(_context, blockUpdate.X, blockUpdate.Y, blockUpdate.Z, meta, blockUpdate.BlockId));
        }
    }

    public void TriggerInstantTick(int x, int y, int z, int blockId)
    {
        var meta = _context.Reader.GetBlockMeta(x, y, z);
        _context.Content.Blocks.GetByProtocolId(blockId).OnTick(new OnTickEvent(_context, x, y, z, meta, blockId));
    }

    /// <summary>
    ///     Returns pending block updates whose (x,z) falls within the given chunk bounds.
    ///     Used for persisting scheduled ticks to chunk NBT (TileTicks) on save.
    /// </summary>
    public IEnumerable<(int X, int Y, int Z, int BlockId, long ScheduledTime, long ScheduledOrder)> GetPendingTicksInChunk(int chunkX, int chunkZ)
    {
        var minX = chunkX * 16;
        var maxX = minX + 15;
        var minZ = chunkZ * 16;
        var maxZ = minZ + 15;

        List<(int X, int Y, int Z, int BlockId, long ScheduledTime, long ScheduledOrder)> pending = [];

        lock (_queueLock)
        {
            foreach (var (blockUpdate, _) in _scheduledUpdates.UnorderedItems)
            {
                if (blockUpdate.X >= minX && blockUpdate.X <= maxX && blockUpdate.Z >= minZ && blockUpdate.Z <= maxZ)
                {
                    pending.Add((blockUpdate.X, blockUpdate.Y, blockUpdate.Z, blockUpdate.BlockId, blockUpdate.ScheduledTime, blockUpdate.ScheduledOrder));
                }
            }
        }

        return pending;
    }

    private readonly record struct ScheduledBlockTick(int X, int Y, int Z, int BlockId);
}
