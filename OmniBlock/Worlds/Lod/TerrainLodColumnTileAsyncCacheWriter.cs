namespace OmniBlock.Worlds.Lod;

public sealed record TerrainLodColumnTileAsyncCacheWriterSnapshot(
    int Capacity,
    int Queued,
    long Submitted,
    long Coalesced,
    long RejectedAtCapacity,
    long Written,
    long Failed,
    string? LastError,
    int Running = 0);

/// <summary>
///     Bounded best-effort persistence lane for spatial column tiles. The most recent candidate
///     for a tile key replaces older queued work; disk latency never owns or stalls live coverage.
/// </summary>
public sealed class TerrainLodColumnTileAsyncCacheWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Func<TerrainLodColumnTile, TerrainLodColumnTileCacheWriteStatus> _write;
    private readonly Dictionary<TerrainLodTileKey, TerrainLodColumnTile> _pending = [];
    private readonly Queue<TerrainLodTileKey> _order = [];
    private readonly Thread _worker;
    private bool _disposed;
    private long _submitted;
    private long _coalesced;
    private long _rejectedAtCapacity;
    private long _written;
    private long _failed;
    private int _running;
    private string? _lastError;
    private TerrainLodColumnTileAsyncCacheWriterSnapshot _snapshot = null!;

    public TerrainLodColumnTileAsyncCacheWriter(
        TerrainLodColumnTileCacheStore store,
        int capacity = 32)
        : this(capacity, RequireStore(store).Write) { }

    internal TerrainLodColumnTileAsyncCacheWriter(
        int capacity,
        Func<TerrainLodColumnTile, TerrainLodColumnTileCacheWriteStatus> write)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _write = write ?? throw new ArgumentNullException(nameof(write));
        PublishSnapshotLocked();
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "TerrainLOD-ColumnCache"
        };
        _worker.Start();
    }

    public bool TrySubmit(TerrainLodColumnTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending.ContainsKey(tile.Key))
            {
                _pending[tile.Key] = tile;
                _coalesced++;
                PublishSnapshotLocked();
                return true;
            }
            if (_pending.Count >= _capacity)
            {
                _rejectedAtCapacity++;
                PublishSnapshotLocked();
                return false;
            }
            _pending.Add(tile.Key, tile);
            _order.Enqueue(tile.Key);
            _submitted++;
            PublishSnapshotLocked();
            Monitor.Pulse(_gate);
            return true;
        }
    }

    public TerrainLodColumnTileAsyncCacheWriterSnapshot Snapshot() =>
        Volatile.Read(ref _snapshot);

    private void WorkerLoop()
    {
        while (true)
        {
            TerrainLodColumnTile tile;
            lock (_gate)
            {
                while (!_disposed && _order.Count == 0) Monitor.Wait(_gate);
                if (_disposed) return;
                var key = _order.Dequeue();
                if (!_pending.Remove(key, out tile!)) continue;
                _running = 1;
                PublishSnapshotLocked();
            }

            try
            {
                var status = _write(tile);
                lock (_gate)
                {
                    _running = 0;
                    if (status == TerrainLodColumnTileCacheWriteStatus.Written)
                    {
                        _written++;
                        _lastError = null;
                    }
                    else
                    {
                        _failed++;
                        _lastError =
                            "Terrain LOD column-tile record exceeded its cache budget.";
                    }
                    PublishSnapshotLocked();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          InvalidDataException or ArgumentException or
                                          InvalidOperationException)
            {
                lock (_gate)
                {
                    _running = 0;
                    _failed++;
                    _lastError = error.GetBaseException().Message;
                    PublishSnapshotLocked();
                }
            }
        }
    }

    private static TerrainLodColumnTileCacheStore RequireStore(
        TerrainLodColumnTileCacheStore? store) =>
        store ?? throw new ArgumentNullException(nameof(store));

    private void PublishSnapshotLocked() => Volatile.Write(ref _snapshot,
        new TerrainLodColumnTileAsyncCacheWriterSnapshot(
            _capacity,
            _pending.Count,
            _submitted,
            _coalesced,
            _rejectedAtCapacity,
            _written,
            _failed,
            _lastError,
            _running));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            _order.Clear();
            PublishSnapshotLocked();
            Monitor.PulseAll(_gate);
        }
        if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(5));
    }
}
