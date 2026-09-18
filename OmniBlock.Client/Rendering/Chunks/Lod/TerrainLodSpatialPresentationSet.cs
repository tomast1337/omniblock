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
    private TerrainLodTileSelection[] _current = [];
    private TerrainLodTileSelection[] _from = [];
    private TerrainLodTileSelection[] _to = [];
    private float _progress = 1;
    private bool _disposed;

    public int Count => _entries.Count;
    public bool Transitioning => _to.Length != 0;

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
        var desired = TerrainLodSpatialSelector.Select(
            root, cameraChunkX, cameraChunkZ, policy, IsReady);

        if (!desired.CompleteCoverage)
        {
            // Never replace a previously complete partition with a hole. This can occur only when
            // callers explicitly evict active coverage; candidate failure alone keeps its entry.
            var retained = Resolve(_current, new TerrainLodSpatialPresentationFade(1, 0, Seed(root)));
            return new TerrainLodSpatialPresentationFrame<TPresentation>(
                retained, retained.Length != 0, transitioning: false);
        }

        var requested = desired.Nodes.ToArray();
        if (_current.Length == 0)
        {
            _current = requested;
            return StableFrame(root, desired.CompleteCoverage);
        }

        if (!SamePartition(requested, Transitioning ? _to : _current))
        {
            var source = Transitioning && _progress >= 0.5f ? _to :
                Transitioning ? _from : _current;
            if (SamePartition(requested, source))
            {
                _current = source;
                _from = [];
                _to = [];
                _progress = 1;
                return StableFrame(root, completeCoverage: true);
            }

            _from = source;
            _to = requested;
            _progress = fadeEnabled ? 0 : 1;
        }

        if (!Transitioning) return StableFrame(root, completeCoverage: true);
        _progress = fadeEnabled
            ? Math.Clamp(_progress + Math.Max(0, deltaTime) / TransitionDurationSeconds, 0, 1)
            : 1;
        if (_progress >= 1)
        {
            _current = _to;
            _from = [];
            _to = [];
            return StableFrame(root, completeCoverage: true);
        }

        var seed = Seed(root);
        var outgoing = Resolve(
            _from, new TerrainLodSpatialPresentationFade(_progress, 2, seed));
        var incoming = Resolve(
            _to, new TerrainLodSpatialPresentationFade(_progress, 1, seed));
        return new TerrainLodSpatialPresentationFrame<TPresentation>(
            [.. outgoing, .. incoming], completeCoverage: true, transitioning: true);
    }

    public void Dispose()
    {
        AssertOwnerThread();
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries.Values) entry.Presentation.Dispose();
        _entries.Clear();
        _current = [];
        _from = [];
        _to = [];
    }

    private TerrainLodSpatialPresentationFrame<TPresentation> StableFrame(
        TerrainLodTileKey root,
        bool completeCoverage) => new(
        Resolve(_current, new TerrainLodSpatialPresentationFade(1, 0, Seed(root))),
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
}
