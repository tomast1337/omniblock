using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>Distinguishes an unloaded source from a new chunk lifetime at the same coordinates.</summary>
internal sealed class TerrainLodSourceLifetime(Chunk source)
{
    private readonly WeakReference<Chunk> _source = new(source);
    public long Revision { get; } = source.TerrainRevision;

    public bool IsCurrent(Chunk? current)
    {
        // The immutable capture remains usable after unload/collection, but never replaces a new
        // chunk instance merely because its local revision counter happens to have the same value.
        if (!_source.TryGetTarget(out var original)) return current is null;
        return original.TerrainRevision == Revision &&
               (current is null || ReferenceEquals(original, current));
    }
}

/// <summary>
/// Render-thread owner of visual snapshots paired with admitted conversion work. The compiler
/// takes ownership only after admission succeeds. Never retain gameplay chunks or overflow the
/// conversion coordinate capacity; coalescing/discard/teardown release the old pooled arrays.
/// </summary>
internal sealed class TerrainLodVisualCaptures(int capacity) : IDisposable
{
    internal sealed record Capture(TerrainLodSourceLifetime Lifetime, WorldRegionSnapshot Visuals);
    private readonly Dictionary<(int X, int Z), Capture> _captures = [];
    public int Count => _captures.Count;
    public long RetainedArrayBytes => _captures.Values.Sum(capture => capture.Visuals.RetainedArrayBytes);

    public void Replace((int X, int Z) key, Capture capture)
    {
        if (!_captures.ContainsKey(key) && _captures.Count >= capacity)
            throw new InvalidOperationException("Visual captures exceeded the conversion capacity.");
        Discard(key);
        _captures.Add(key, capture);
    }

    public bool TryGet((int X, int Z) key, out Capture capture) => _captures.TryGetValue(key, out capture!);

    public void Discard((int X, int Z) key)
    {
        if (_captures.Remove(key, out var capture)) capture.Visuals.Dispose();
    }

    public void TransferToCompiler((int X, int Z) key)
    {
        if (!_captures.Remove(key)) throw new InvalidOperationException("Missing admitted visual capture.");
    }

    public void Dispose()
    {
        foreach (var capture in _captures.Values) capture.Visuals.Dispose();
        _captures.Clear();
    }
}
