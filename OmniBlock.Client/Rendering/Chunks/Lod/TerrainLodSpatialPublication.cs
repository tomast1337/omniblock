using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal readonly record struct PublishedTerrainSeam<T>(T Presentation, TerrainLodSpatialPresentationFade Fade);

/// <summary>
/// Holds one complete displayed body/seam snapshot while the next partition is being prepared.
/// Pins include replaced catalog entries; retaining just their coordinates is insufficient.
/// </summary>
internal sealed class TerrainLodSpatialPublication<TBody, TSeam> : IDisposable
    where TBody : RetainedTerrainResource
    where TSeam : RetainedTerrainResource
{
    private List<IDisposable> _leases = [];
    public TerrainLodSpatialPresentationFrame<TBody>? Frame { get; private set; }
    public IReadOnlyDictionary<TerrainLodSpatialSeamSegment, PublishedTerrainSeam<TSeam>> Seams { get; private set; } =
        new Dictionary<TerrainLodSpatialSeamSegment, PublishedTerrainSeam<TSeam>>();

    public bool TryPublish(
        TerrainLodSpatialPresentationFrame<TBody> frame,
        IReadOnlyDictionary<TerrainLodSpatialSeamSegment, PublishedTerrainSeam<TSeam>> seams,
        bool seamsReady)
    {
        if (!frame.CompleteCoverage || !seamsReady) return false;
        if (ReferenceEquals(frame, Frame)) return true;

        var seamSnapshot = new Dictionary<TerrainLodSpatialSeamSegment, PublishedTerrainSeam<TSeam>>(seams);
        List<IDisposable> acquired = [];
        try
        {
            foreach (var body in frame.Draws.Select(static draw => draw.Presentation).Distinct())
                acquired.Add(body.Retain());
            foreach (var seam in seamSnapshot.Values.Select(static seam => seam.Presentation).Distinct())
                acquired.Add(seam.Retain());
        }
        catch
        {
            foreach (var lease in acquired) lease.Dispose();
            throw;
        }

        var previous = _leases;
        Frame = frame;
        Seams = seamSnapshot;
        _leases = acquired;
        foreach (var lease in previous) lease.Dispose();
        return true;
    }

    public void Dispose()
    {
        Frame = null;
        Seams = new Dictionary<TerrainLodSpatialSeamSegment, PublishedTerrainSeam<TSeam>>();
        foreach (var lease in _leases) lease.Dispose();
        _leases.Clear();
    }
}
