using Microsoft.Extensions.Logging;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal enum ClientTerrainLodCacheProbe
{
    Pending,
    Missing,
    Available
}

/// <summary>
///     Bounded disk lane for server-origin spatial tiles. Reads and durable writes never run on
///     the render or network thread. A cached tile is only an offer: the renderer must wait for
///     the server's matching-hash response before it may publish the tile as terrain coverage.
/// </summary>
internal sealed class ClientTerrainLodSpatialCache : IDisposable
{
    private const int MaximumPendingReads = 32;
    private const int MaximumPendingWrites = 32;
    private const int MaximumLoadedResults = 32;
    private const long MaximumReadWaitMs = 250;
    private readonly ILogger _logger = Log.Instance.For<ClientTerrainLodSpatialCache>();
    private readonly object _gate = new();
    private readonly DirectoryInfo _root;
    private readonly TerrainLodCacheIdentity _identity;
    private readonly Thread _worker;
    private readonly Queue<TerrainLodTileKey> _reads = [];
    private readonly Dictionary<TerrainLodTileKey, long> _readPending = [];
    private readonly Dictionary<TerrainLodTileKey, TerrainLodColumnTile?> _results = [];
    private readonly HashSet<TerrainLodTileKey> _offerDisabled = [];
    private readonly Queue<TerrainLodTileKey> _writeOrder = [];
    private readonly Dictionary<TerrainLodTileKey, TerrainLodColumnTile> _writes = [];
    private bool _disposed;
    private bool _failed;

    public ClientTerrainLodSpatialCache(DirectoryInfo root, TerrainLodCacheIdentity identity)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "TerrainLOD-ClientCache",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public ClientTerrainLodCacheProbe Probe(TerrainLodTileKey key, out string hash)
    {
        lock (_gate)
        {
            hash = "";
            if (_disposed || _failed || _offerDisabled.Contains(key))
                return ClientTerrainLodCacheProbe.Missing;
            if (_results.TryGetValue(key, out var tile))
            {
                if (tile is null) return ClientTerrainLodCacheProbe.Missing;
                hash = tile.CanonicalHash;
                return ClientTerrainLodCacheProbe.Available;
            }
            if (_readPending.TryGetValue(key, out var started))
            {
                if (Environment.TickCount64 - started < MaximumReadWaitMs)
                    return ClientTerrainLodCacheProbe.Pending;
                _offerDisabled.Add(key);
                return ClientTerrainLodCacheProbe.Missing;
            }
            if (_readPending.Count >= MaximumPendingReads)
                return ClientTerrainLodCacheProbe.Missing;
            _readPending.Add(key, Environment.TickCount64);
            _reads.Enqueue(key);
            Monitor.Pulse(_gate);
            return ClientTerrainLodCacheProbe.Pending;
        }
    }

    public string OfferedHash(TerrainLodTileKey key)
    {
        lock (_gate)
            return _results.TryGetValue(key, out var tile) ? tile?.CanonicalHash ?? "" : "";
    }

    public bool TryGetValidatedTile(TerrainLodTileKey key, out TerrainLodColumnTile? tile)
    {
        lock (_gate)
        {
            if (_results.TryGetValue(key, out tile) && tile is not null)
            {
                return true;
            }
            tile = null;
            return false;
        }
    }

    public void DisableOffer(TerrainLodTileKey key)
    {
        lock (_gate)
        {
            _results.Remove(key);
            _offerDisabled.Add(key);
        }
    }

    public void QueueWrite(TerrainLodColumnTile tile)
    {
        lock (_gate)
        {
            if (_disposed || _failed) return;
            _offerDisabled.Remove(tile.Key);
            _results.Remove(tile.Key);
            if (!_writes.ContainsKey(tile.Key))
            {
                if (_writes.Count >= MaximumPendingWrites) return;
                _writeOrder.Enqueue(tile.Key);
            }
            _writes[tile.Key] = tile;
            Monitor.Pulse(_gate);
        }
    }

    private void Work()
    {
        TerrainLodColumnTileCacheStore store;
        try
        {
            store = new TerrainLodColumnTileCacheStore(
                new DirectoryInfo(Path.Combine(_root.FullName, _identity.RecordFingerprint)),
                _identity, 512L * 1024 * 1024, 4L * 1024 * 1024);
        }
        catch (Exception error)
        {
            Fail(error);
            return;
        }

        var readsSinceWrite = 0;
        while (true)
        {
            TerrainLodTileKey key = default;
            TerrainLodColumnTile? write = null;
            var read = false;
            lock (_gate)
            {
                while (!_disposed && _reads.Count == 0 && _writeOrder.Count == 0)
                    Monitor.Wait(_gate);
                if (_disposed && _writeOrder.Count == 0) return;
                if (_writeOrder.Count > 0 &&
                    (_disposed || _reads.Count == 0 || readsSinceWrite >= 4) &&
                    _writeOrder.TryDequeue(out key))
                {
                    write = _writes[key];
                    _writes.Remove(key);
                    readsSinceWrite = 0;
                }
                else if (!_disposed && _reads.TryDequeue(out key))
                {
                    read = true;
                    readsSinceWrite++;
                }
            }
            if (read)
            {
                TerrainLodColumnTile? tile = null;
                try
                {
                    tile = store.Read(key).Tile;
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Could not read cached terrain LOD tile {Tile}.", key);
                }
                lock (_gate)
                {
                    _readPending.Remove(key);
                    if (_disposed || _offerDisabled.Contains(key)) continue;
                    if (!_results.ContainsKey(key) && _results.Count >= MaximumLoadedResults)
                        _results.Remove(_results.Keys.First());
                    _results[key] = tile;
                }
            }
            else if (write is not null)
            {
                try
                {
                    store.Write(write);
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Could not persist terrain LOD tile {Tile}.", key);
                }
            }
        }
    }

    private void Fail(Exception error)
    {
        _logger.LogWarning(error, "Client terrain LOD spatial cache is unavailable.");
        lock (_gate)
        {
            _failed = true;
            _reads.Clear();
            _readPending.Clear();
            _writes.Clear();
            _writeOrder.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _reads.Clear();
            Monitor.PulseAll(_gate);
        }
        // Accepted writes continue on the background worker if storage is slower than the
        // shutdown grace period. Never block the client's close indefinitely on disk I/O.
        _worker.Join(TimeSpan.FromSeconds(2));
    }
}
