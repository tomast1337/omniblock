using System.Collections.ObjectModel;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal readonly record struct TerrainLodSpatialPresentationFade(
    float Progress,
    uint Mode,
    uint Seed);

internal readonly record struct TerrainLodSpatialPresentationDraw<TPresentation>(
    TerrainLodTileSelection Selection,
    TPresentation Presentation,
    TerrainLodSpatialPresentationFade Fade)
    where TPresentation : class, IDisposable;

internal sealed class TerrainLodSpatialPresentationFrame<TPresentation>
    where TPresentation : class, IDisposable
{
    internal TerrainLodSpatialPresentationFrame(
        TerrainLodSpatialPresentationDraw<TPresentation>[] draws,
        bool completeCoverage,
        bool transitioning)
    {
        Draws = Array.AsReadOnly(draws);
        CompleteCoverage = completeCoverage;
        Transitioning = transitioning;
    }

    public ReadOnlyCollection<TerrainLodSpatialPresentationDraw<TPresentation>> Draws { get; }
    public bool CompleteCoverage { get; }
    public bool Transitioning { get; }
}

/// <summary>
///     Render-thread owner for completed spatial GPU presentations. Candidate construction happens
///     before publication; a failed factory therefore cannot disturb the active predecessor. The
///     pure hierarchy selector supplies all-children-or-parent coverage, while this owner fades the
///     complete old and new partitions with one shared mask.
/// </summary>
internal sealed class TerrainLodSpatialPresentationSet<TPresentation> : IDisposable
    where TPresentation : class, IDisposable
{
    internal const float TransitionDurationSeconds = 0.25f;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<TerrainLodTileKey, Entry> _entries = [];
    private readonly Dictionary<TerrainLodTileKey, TransitionState> _transitions = [];
    private bool _disposed;

    public int Count => _entries.Count;
    public long Revision { get; private set; }
    public bool Transitioning => _transitions.Values.Any(static state => state.To.Length != 0);
    public IEnumerable<TerrainLodTileKey> ReadyKeys => _entries.Keys;
    public IEnumerable<TPresentation> ReadyPresentations =>
        _entries.Values.Select(static entry => entry.Presentation);

    public bool TryInstall(
        TerrainLodTileKey key,
        string canonicalHash,
        Func<TPresentation> createCandidate,
        out Exception? failure)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalHash);
        ArgumentNullException.ThrowIfNull(createCandidate);
        failure = null;

        if (_entries.TryGetValue(key, out var unchanged) &&
            string.Equals(unchanged.CanonicalHash, canonicalHash, StringComparison.Ordinal))
            return false;

        TPresentation candidate;
        try
        {
            candidate = createCandidate() ?? throw new InvalidOperationException(
                $"Spatial terrain presentation factory returned null for {key}.");
        }
        catch (Exception error)
        {
            failure = error;
            return false;
        }

        // The dictionary swap is the publication point. Only after it succeeds may the previous
        // buffers retire; until then every selector sees the predecessor as valid coverage.
        _entries[key] = new Entry(canonicalHash, candidate);
        Revision++;
        unchanged?.Presentation.Dispose();
        return true;
    }

    public bool IsReady(TerrainLodTileKey key) => _entries.ContainsKey(key);

    public TerrainLodSpatialPresentationFrame<TPresentation> Update(
        TerrainLodTileKey root,
        double cameraChunkX,
        double cameraChunkZ,
        TerrainLodSpatialPolicy policy,
        float deltaTime,
        bool fadeEnabled)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(policy);
        if (!_transitions.TryGetValue(root, out var state))
        {
            state = new TransitionState();
            _transitions.Add(root, state);
        }
        var desired = TerrainLodSpatialSelector.Select(
            root, cameraChunkX, cameraChunkZ, policy, IsReady);

        if (!desired.CompleteCoverage)
        {
            // Never replace a previously complete partition with a hole. This can occur only when
            // callers explicitly evict active coverage; candidate failure alone keeps its entry.
            var retained = Resolve(
                state.Current, new TerrainLodSpatialPresentationFade(1, 0, Seed(root)));
            return new TerrainLodSpatialPresentationFrame<TPresentation>(
                retained, retained.Length != 0, transitioning: false);
        }

        return UpdatePartition(
            root, desired.Nodes, completeRootCoverage: true,
            deltaTime, fadeEnabled);
    }

    /// <summary>
    ///     Publishes a forest partition within one stable management root. Partial management
    ///     roots grow without fading because old and new partitions do not cover the same area.
    ///     Once both partitions cover the complete root, parent/child promotion uses the shared
    ///     group transition instead of independently replacing quadrants.
    /// </summary>
    public TerrainLodSpatialPresentationFrame<TPresentation> UpdatePartition(
        TerrainLodTileKey root,
        IReadOnlyList<TerrainLodTileSelection> desired,
        bool completeRootCoverage,
        float deltaTime,
        bool fadeEnabled)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(desired);
        if (!_transitions.TryGetValue(root, out var state))
        {
            state = new TransitionState();
            _transitions.Add(root, state);
        }

        var requested = desired.ToArray();
        if (requested.Any(selection => !_entries.ContainsKey(selection.Tile)))
            throw new InvalidOperationException(
                $"Spatial terrain forest for {root} selected a presentation that is not resident.");

        if (state.Current.Length == 0)
        {
            state.Current = requested;
            state.CurrentCompleteRootCoverage = completeRootCoverage;
            return StableFrame(root, state, requested.Length != 0);
        }

        // A partial management domain can gain or lose independently covered descendants. There
        // is no complementary old geometry for newly covered space, so cross-fading would create
        // deliberate holes. Publish that set directly; the caller still gates authority on the
        // complete replacement seam set.
        if (!completeRootCoverage || !state.CurrentCompleteRootCoverage)
        {
            state.Current = requested;
            state.CurrentCompleteRootCoverage = completeRootCoverage;
            state.From = [];
            state.To = [];
            state.Progress = 1;
            return StableFrame(root, state, requested.Length != 0);
        }

        if (!SamePartition(requested, state.To.Length != 0 ? state.To : state.Current))
        {
            var source = state.To.Length != 0 && state.Progress >= 0.5f ? state.To :
                state.To.Length != 0 ? state.From : state.Current;
            if (SamePartition(requested, source))
            {
                state.Current = source;
                state.CurrentCompleteRootCoverage = completeRootCoverage;
                state.From = [];
                state.To = [];
                state.Progress = 1;
                return StableFrame(root, state, completeCoverage: true);
            }

            state.From = source;
            state.To = requested;
            state.Progress = fadeEnabled ? 0 : 1;
        }

        if (state.To.Length == 0) return StableFrame(root, state, completeCoverage: true);
        state.Progress = fadeEnabled
            ? Math.Clamp(state.Progress + Math.Max(0, deltaTime) / TransitionDurationSeconds, 0, 1)
            : 1;
        if (state.Progress >= 1)
        {
            state.Current = state.To;
            state.CurrentCompleteRootCoverage = completeRootCoverage;
            state.From = [];
            state.To = [];
            return StableFrame(root, state, completeCoverage: true);
        }

        var seed = Seed(root);
        var outgoing = Resolve(
            state.From, new TerrainLodSpatialPresentationFade(state.Progress, 2, seed));
        var incoming = Resolve(
            state.To, new TerrainLodSpatialPresentationFade(state.Progress, 1, seed));
        return new TerrainLodSpatialPresentationFrame<TPresentation>(
            [.. outgoing, .. incoming], completeCoverage: true, transitioning: true);
    }

    public void RetainTransitionRoots(IReadOnlySet<TerrainLodTileKey> activeRoots)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activeRoots);
        foreach (var root in _transitions.Keys.Where(root => !activeRoots.Contains(root)).ToArray())
            _transitions.Remove(root);
    }

    public void Dispose()
    {
        AssertOwnerThread();
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries.Values) entry.Presentation.Dispose();
        _entries.Clear();
        _transitions.Clear();
    }

    private TerrainLodSpatialPresentationFrame<TPresentation> StableFrame(
        TerrainLodTileKey root,
        TransitionState state,
        bool completeCoverage) => new(
        Resolve(state.Current, new TerrainLodSpatialPresentationFade(1, 0, Seed(root))),
        completeCoverage,
        transitioning: false);

    private TerrainLodSpatialPresentationDraw<TPresentation>[] Resolve(
        IEnumerable<TerrainLodTileSelection> selections,
        TerrainLodSpatialPresentationFade fade) => selections
        .Select(selection => new TerrainLodSpatialPresentationDraw<TPresentation>(
            selection,
            _entries.TryGetValue(selection.Tile, out var entry)
                ? entry.Presentation
                : throw new InvalidOperationException(
                    $"Selected spatial terrain presentation {selection.Tile} is no longer resident."),
            fade))
        .ToArray();

    private static bool SamePartition(
        IReadOnlyList<TerrainLodTileSelection> first,
        IReadOnlyList<TerrainLodTileSelection> second)
    {
        if (first.Count != second.Count) return false;
        for (var index = 0; index < first.Count; index++)
            if (first[index] != second[index]) return false;
        return true;
    }

    private static uint Seed(TerrainLodTileKey root) => unchecked((uint)(
        root.Level * 83_492_791 ^ root.X * 73_856_093 ^ root.Z * 19_349_663));

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "Spatial terrain GPU presentations are owned by the render thread.");
    }

    private sealed record Entry(string CanonicalHash, TPresentation Presentation);

    private sealed class TransitionState
    {
        public TerrainLodTileSelection[] Current = [];
        public TerrainLodTileSelection[] From = [];
        public TerrainLodTileSelection[] To = [];
        public float Progress = 1;
        public bool CurrentCompleteRootCoverage;
    }
}

internal static class TerrainLodSpatialAuthority
{
    internal const ulong AllColumnsHidden = (1UL << 36) - 1;

    // A 64-block page spans 4x4 columns. Include a one-column halo because vertical seam
    // geometry can lie exactly on a page edge and belong to the column on either side.
    public static ulong HiddenColumnMask(int pageChunkX, int pageChunkZ, Func<int, int, bool> spatialOwns)
    {
        ulong mask = 0;
        for (var z = -1; z <= 4; z++)
        for (var x = -1; x <= 4; x++)
            if (!spatialOwns(pageChunkX + x, pageChunkZ + z))
                mask |= 1UL << ((z + 1) * 6 + x + 1);
        return mask;
    }

    /// <summary>
    ///     Distance requests a handoff; complete GPU coverage permits it. In particular, entering
    ///     the guard band cannot discard a remotely supplied tile whose columns have not streamed
    ///     into the near renderer or the legacy LOD cache yet.
    /// </summary>
    public static bool ShouldPresent(
        TerrainLodTileKey tile,
        double cameraChunkX,
        double cameraChunkZ,
        int renderDistance,
        Func<int, int, bool> replacementReady)
    {
        if (IsBeyondNearRadius(tile, cameraChunkX, cameraChunkZ, renderDistance)) return true;
        for (var z = tile.MinChunkZ; z <= tile.MaxChunkZ; z++)
        for (var x = tile.MinChunkX; x <= tile.MaxChunkX; x++)
            if (!replacementReady(checked((int)x), checked((int)z))) return true;
        return false;
    }

    /// <summary>
    ///     Beyond the exact-render radius plus one guard chunk, spatial tiles own the full
    ///     footprint. Within it, authority is decided per column from replacement readiness.
    /// </summary>
    public static bool IsBeyondNearRadius(
        TerrainLodTileKey tile,
        double cameraChunkX,
        double cameraChunkZ,
        int renderDistance) =>
        tile.DistanceTo(cameraChunkX, cameraChunkZ) >= Math.Max(0, renderDistance) + 1.0;
}
