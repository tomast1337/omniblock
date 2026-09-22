using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
/// Keeps a bounded, immutable fine source for nearby coarse presentations. A quality increase can
/// rebuild level zero after gameplay has unloaded the chunk, without requesting gameplay terrain.
/// The conversion worker and this owner never share a mutable visual snapshot.
/// </summary>
internal sealed class TerrainLodRefinementSources(long byteBudget, double radiusBlocks) : IDisposable
{
    internal sealed record Source(
        TerrainLodSourceSnapshot Terrain,
        WorldRegionSnapshot Visuals,
        TerrainLodSourceLifetime Lifetime)
    {
        public long RetainedBytes => Terrain.EstimatedBytes + Visuals.RetainedArrayBytes;
    }

    private readonly Dictionary<(int X, int Z), Source> _sources = [];
    private long _retainedBytes;

    public int Count => _sources.Count;
    public long RetainedBytes => _retainedBytes;

    public bool TryGet((int X, int Z) key, out Source source) =>
        _sources.TryGetValue(key, out source!);

    public void Retain(
        (int X, int Z) key,
        TerrainLodVisualCaptures.Capture capture,
        double viewX,
        double viewZ)
    {
        if (capture.Source is null ||
            DistanceSquared(key, viewX, viewZ) > radiusBlocks * radiusBlocks)
            return;

        var visuals = capture.Visuals.Clone();
        var candidate = new Source(capture.Source, visuals, capture.Lifetime);
        if (candidate.RetainedBytes > byteBudget)
        {
            visuals.Dispose();
            return;
        }

        Remove(key);
        _sources.Add(key, candidate);
        _retainedBytes += candidate.RetainedBytes;
        Prune(viewX, viewZ);
    }

    public void Prune(double viewX, double viewZ)
    {
        foreach (var key in _sources.Keys.Where(key =>
                     DistanceSquared(key, viewX, viewZ) > radiusBlocks * radiusBlocks).ToArray())
            Remove(key);

        while (_retainedBytes > byteBudget)
        {
            var farthest = _sources.Keys
                .OrderByDescending(key => DistanceSquared(key, viewX, viewZ))
                .ThenBy(key => key.X)
                .ThenBy(key => key.Z)
                .First();
            Remove(farthest);
        }
    }

    public void Remove((int X, int Z) key)
    {
        if (!_sources.Remove(key, out var source)) return;
        _retainedBytes -= source.RetainedBytes;
        source.Visuals.Dispose();
    }

    public void Dispose()
    {
        foreach (var source in _sources.Values) source.Visuals.Dispose();
        _sources.Clear();
        _retainedBytes = 0;
    }

    private static double DistanceSquared((int X, int Z) key, double viewX, double viewZ)
    {
        var dx = key.X * 16 + 8 - viewX;
        var dz = key.Z * 16 + 8 - viewZ;
        return dx * dx + dz * dz;
    }
}
