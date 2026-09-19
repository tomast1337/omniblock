namespace OmniBlock.Worlds.Lod;

public enum TerrainLodTilePublicationResult
{
    Added,
    Replaced,
    IgnoredCurrent,
    RejectedAtCapacity
}

public sealed record TerrainLodSpatialHierarchyCoordinatorSnapshot(
    int TileCapacity,
    int Tiles,
    int CurrentTiles,
    int FallbackTiles,
    int Leaves,
    int Parents,
    int DeferredParents,
    long LeafPublications,
    long CachedPublications,
    long CachedParentPromotions,
    long ParentPublications,
    long StaleCandidatesRejected,
    long TileCapacityRejections,
    long ConstructionCapacityDeferrals,
    long PersistenceDeferrals,
    long Evictions,
    TerrainLodParentConstructionSnapshot Construction,
    TerrainLodColumnTileAsyncCacheWriterSnapshot? Persistence);

/// <summary>
///     Coordinates immutable leaf records, parent construction, and cache persistence without
///     confusing candidates with coverage. A node's previous presentation remains available while
///     its <c>Current</c> flag is false and a replacement is queued or running. Only four current,
///     hash-matching children may publish a current parent or wake the next ancestor.
/// </summary>
public sealed class TerrainLodSpatialHierarchyCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly TerrainLodSpatialPolicy _policy;
    private readonly TerrainLodParentConstructionService _construction;
    private readonly TerrainLodColumnTileAsyncCacheWriter? _persistence;
    private readonly int _tileCapacity;
    private readonly bool _ownsDependencies;
    private readonly Dictionary<TerrainLodTileKey, TileNode> _nodes = [];
    private readonly HashSet<TerrainLodTileKey> _deferredParents = [];
    private bool _disposed;
    private double _cameraChunkX;
    private double _cameraChunkZ;
    private long _leafPublications;
    private long _cachedPublications;
    private long _cachedParentPromotions;
    private long _parentPublications;
    private long _staleCandidatesRejected;
    private long _tileCapacityRejections;
    private long _constructionCapacityDeferrals;
    private long _persistenceDeferrals;
    private long _evictions;

    public TerrainLodSpatialHierarchyCoordinator(
        TerrainLodSpatialPolicy policy,
        TerrainLodColumnTileCacheStore? cache = null,
        int tileCapacity = 4096,
        int constructionCapacity = 64,
        int completedCapacity = 16,
        int persistenceCapacity = 32)
        : this(
            policy,
            new TerrainLodParentConstructionService(
                constructionCapacity, completedCapacity),
            cache is null
                ? null
                : new TerrainLodColumnTileAsyncCacheWriter(cache, persistenceCapacity),
            tileCapacity,
            ownsDependencies: true) { }

    internal TerrainLodSpatialHierarchyCoordinator(
        TerrainLodSpatialPolicy policy,
        TerrainLodParentConstructionService construction,
        TerrainLodColumnTileAsyncCacheWriter? persistence,
        int tileCapacity,
        bool ownsDependencies)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _construction = construction ?? throw new ArgumentNullException(nameof(construction));
        if (tileCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(tileCapacity));
        _persistence = persistence;
        _tileCapacity = tileCapacity;
        _ownsDependencies = ownsDependencies;
    }

    public void SetCameraChunkPosition(double chunkX, double chunkZ)
    {
        if (!double.IsFinite(chunkX)) throw new ArgumentOutOfRangeException(nameof(chunkX));
        if (!double.IsFinite(chunkZ)) throw new ArgumentOutOfRangeException(nameof(chunkZ));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cameraChunkX = chunkX;
            _cameraChunkZ = chunkZ;
        }
    }

    public TerrainLodTilePublicationResult PublishLeaf(TerrainLodColumnTile leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        if (leaf.Key.Level != 0 || leaf.LeafTerrainRevision is null)
            throw new ArgumentException(
                "A published terrain LOD leaf must be a level-zero revisioned tile.",
                nameof(leaf));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var publication = InstallLocked(leaf, current: true, ignoreCurrent: false);
            if (publication == TerrainLodTilePublicationResult.RejectedAtCapacity)
                return publication;
            _leafPublications++;
            PersistLocked(leaf);
            InvalidateAndScheduleAncestorsLocked(leaf.Key);
            return publication;
        }
    }

    /// <summary>
    ///     Installs durable data as fallback. A parent becomes current only when its four current
    ///     in-memory children reproduce its ordered input hashes; otherwise it remains presentation
    ///     coverage and is never consumed by another build.
    /// </summary>
    public TerrainLodTilePublicationResult PublishCached(TerrainLodColumnTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.Key.Level > _policy.MaximumSpatialLevel)
            throw new ArgumentOutOfRangeException(nameof(tile),
                $"Tile level {tile.Key.Level} exceeds the configured maximum " +
                $"{_policy.MaximumSpatialLevel}.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_nodes.TryGetValue(tile.Key, out var existing) && existing.Current)
                return TerrainLodTilePublicationResult.IgnoredCurrent;
            // Cache identity alone cannot prove a leaf still matches authoritative terrain. A
            // validated cache hit may be handed to PublishLeaf; an unvalidated cached leaf stays
            // fallback-only and cannot seed parents.
            var current = tile.Key.Level > 0 && MatchesCurrentChildrenLocked(tile);
            var publication = InstallLocked(tile, current, ignoreCurrent: true);
            if (publication == TerrainLodTilePublicationResult.RejectedAtCapacity)
                return publication;
            _cachedPublications++;
            if (current) InvalidateAndScheduleAncestorsLocked(tile.Key);
            return publication;
        }
    }

    /// <summary>Publishes at most <paramref name="maximumResults"/> valid CPU candidates.</summary>
    public int DrainCompleted(
        int maximumResults,
        ICollection<TerrainLodColumnTile>? publications = null)
    {
        if (maximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        var published = 0;
        while (published < maximumResults && _construction.TryTakeCompleted(out var result))
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var tile = result!.Tile;
                if (!MatchesCurrentChildrenLocked(tile))
                {
                    _staleCandidatesRejected++;
                    ScheduleParentLocked(tile.Key, allowBeyondMaximum: false);
                    continue;
                }
                if (!_nodes.ContainsKey(tile.Key) && _nodes.Count >= _tileCapacity)
                {
                    _tileCapacityRejections++;
                    _deferredParents.Add(tile.Key);
                    continue;
                }

                InstallLocked(tile, current: true, ignoreCurrent: false);
                _parentPublications++;
                publications?.Add(tile);
                PersistLocked(tile);
                published++;
                if (tile.Key.Level < _policy.MaximumSpatialLevel)
                    ScheduleParentLocked(tile.Key.Parent(), allowBeyondMaximum: false);
                RetryDeferredLocked();
            }
        }
        return published;
    }

    public bool TryGetCoverage(
        TerrainLodTileKey key,
        out TerrainLodColumnTile? tile,
        out bool current)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                tile = node.Tile;
                current = node.Current;
                return true;
            }
            tile = null;
            current = false;
            return false;
        }
    }

    public bool IsCurrent(TerrainLodTileKey key)
    {
        lock (_gate)
            return _nodes.TryGetValue(key, out var node) && node.Current;
    }

    public bool Evict(TerrainLodTileKey key)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_nodes.Remove(key)) return false;
            _construction.Discard(key);
            _deferredParents.Remove(key);
            _evictions++;
            InvalidateAndScheduleAncestorsLocked(key);
            if (key.Level > 0) ScheduleParentLocked(key, allowBeyondMaximum: false);
            RetryDeferredLocked();
            return true;
        }
    }

    public TerrainLodSpatialHierarchyCoordinatorSnapshot Snapshot()
    {
        lock (_gate)
            return new TerrainLodSpatialHierarchyCoordinatorSnapshot(
                _tileCapacity,
                _nodes.Count,
                _nodes.Values.Count(static node => node.Current),
                _nodes.Values.Count(static node => !node.Current),
                _nodes.Keys.Count(static key => key.Level == 0),
                _nodes.Keys.Count(static key => key.Level > 0),
                _deferredParents.Count,
                _leafPublications,
                _cachedPublications,
                _cachedParentPromotions,
                _parentPublications,
                _staleCandidatesRejected,
                _tileCapacityRejections,
                _constructionCapacityDeferrals,
                _persistenceDeferrals,
                _evictions,
                _construction.Snapshot(),
                _persistence?.Snapshot());
    }

    private TerrainLodTilePublicationResult InstallLocked(
        TerrainLodColumnTile tile,
        bool current,
        bool ignoreCurrent)
    {
        if (_nodes.TryGetValue(tile.Key, out var existing))
        {
            if (ignoreCurrent && existing.Current)
                return TerrainLodTilePublicationResult.IgnoredCurrent;
            existing.Tile = tile;
            existing.Current = current;
            return TerrainLodTilePublicationResult.Replaced;
        }
        if (_nodes.Count >= _tileCapacity)
        {
            _tileCapacityRejections++;
            return TerrainLodTilePublicationResult.RejectedAtCapacity;
        }
        _nodes.Add(tile.Key, new TileNode(tile, current));
        return TerrainLodTilePublicationResult.Added;
    }

    private void InvalidateAndScheduleAncestorsLocked(TerrainLodTileKey changed)
    {
        if (changed.Level >= _policy.MaximumSpatialLevel) return;
        var ancestor = changed.Parent();
        while (true)
        {
            if (_nodes.TryGetValue(ancestor, out var node)) node.Current = false;
            if (ancestor.Level == _policy.MaximumSpatialLevel) break;
            ancestor = ancestor.Parent();
        }

        ancestor = changed.Parent();
        while (ancestor.Level <= _policy.MaximumSpatialLevel)
        {
            if (!ScheduleParentLocked(ancestor, allowBeyondMaximum: false))
                _construction.Discard(ancestor);
            if (ancestor.Level == _policy.MaximumSpatialLevel) break;
            ancestor = ancestor.Parent();
        }
    }

    private bool ScheduleParentLocked(TerrainLodTileKey parent, bool allowBeyondMaximum)
    {
        if (parent.Level == 0 ||
            (!allowBeyondMaximum && parent.Level > _policy.MaximumSpatialLevel))
            return false;
        if (!TryGetCurrentChildrenLocked(parent, out var children))
        {
            _deferredParents.Remove(parent);
            return false;
        }
        if (!_nodes.ContainsKey(parent) && _nodes.Count >= _tileCapacity)
        {
            _tileCapacityRejections++;
            _deferredParents.Add(parent);
            return false;
        }
        if (_nodes.TryGetValue(parent, out var cached) &&
            cached.Tile.InputHashes.SequenceEqual(
                children.Select(static child => child.CanonicalHash)))
        {
            cached.Current = true;
            _cachedParentPromotions++;
            _construction.Discard(parent);
            _deferredParents.Remove(parent);
            if (parent.Level < _policy.MaximumSpatialLevel)
                ScheduleParentLocked(parent.Parent(), allowBeyondMaximum: false);
            return true;
        }
        var workKind = _nodes.ContainsKey(parent)
            ? TerrainLodParentWorkKind.Refinement
            : TerrainLodParentWorkKind.Coverage;
        var admission = _construction.Submit(
            parent,
            children,
            _policy.HorizontalSampleLevelForSpatialLevel(parent.Level),
            workKind,
            parent.DistanceTo(_cameraChunkX, _cameraChunkZ));
        if (admission == TerrainLodParentAdmissionResult.RejectedAtCapacity)
        {
            _constructionCapacityDeferrals++;
            _deferredParents.Add(parent);
            return false;
        }
        _deferredParents.Remove(parent);
        return true;
    }

    private bool TryGetCurrentChildrenLocked(
        TerrainLodTileKey parent,
        out TerrainLodColumnTile[] children)
    {
        children = new TerrainLodColumnTile[4];
        for (var index = 0; index < children.Length; index++)
        {
            if (!_nodes.TryGetValue(parent.Child(index), out var child) || !child.Current)
                return false;
            children[index] = child.Tile;
        }
        return true;
    }

    private bool MatchesCurrentChildrenLocked(TerrainLodColumnTile parent)
    {
        if (parent.Key.Level == 0) return parent.LeafTerrainRevision.HasValue;
        if (!TryGetCurrentChildrenLocked(parent.Key, out var children)) return false;
        return parent.InputHashes.SequenceEqual(
            children.Select(static child => child.CanonicalHash));
    }

    private void RetryDeferredLocked()
    {
        if (_deferredParents.Count == 0) return;
        foreach (var key in _deferredParents
                     .OrderByDescending(static key => key.Level)
                     .ThenBy(key => key.DistanceTo(_cameraChunkX, _cameraChunkZ))
                     .ThenBy(static key => key.X)
                     .ThenBy(static key => key.Z)
                     .ToArray())
        {
            if (_nodes.Count >= _tileCapacity && !_nodes.ContainsKey(key)) break;
            ScheduleParentLocked(key, allowBeyondMaximum: false);
        }
    }

    private void PersistLocked(TerrainLodColumnTile tile)
    {
        if (_persistence is not null && !_persistence.TrySubmit(tile))
            _persistenceDeferrals++;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _nodes.Clear();
            _deferredParents.Clear();
        }
        if (!_ownsDependencies) return;
        _construction.Dispose();
        _persistence?.Dispose();
    }

    private sealed class TileNode(TerrainLodColumnTile tile, bool current)
    {
        public TerrainLodColumnTile Tile = tile;
        public bool Current = current;
    }
}
