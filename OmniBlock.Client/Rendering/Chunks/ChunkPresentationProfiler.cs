namespace OmniBlock.Client.Rendering.Chunks;

internal readonly record struct FrameTimingSnapshot(
    int Samples,
    double LastMs,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double MaxMs)
{
    public static FrameTimingSnapshot Empty => new(0, 0, 0, 0, 0, 0);
}

/// <summary>
///     Fixed-size render-thread timing history. Visibility diagnostics must remain cheap enough to
///     leave enabled: recording performs no allocation, while the debug snapshot sorts at most 240
///     copied values only when it is requested.
/// </summary>
internal sealed class FrameTimingWindow(int capacity = 240)
{
    private readonly double[] _samples = new double[capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private readonly double[] _sorted = new double[capacity > 0 ? capacity : 1];
    private int _count;
    private int _next;
    private double _total;

    public void Record(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0) return;

        if (_count == _samples.Length)
        {
            var outgoing = _samples[_next];
            var removeAt = Array.BinarySearch(_sorted, 0, _count, outgoing);
            if (removeAt < 0) throw new InvalidOperationException("Timing window index is inconsistent.");
            Array.Copy(_sorted, removeAt + 1, _sorted, removeAt, _count - removeAt - 1);
            _total -= outgoing;
            InsertSorted(milliseconds, _count - 1);
        }
        else
        {
            InsertSorted(milliseconds, _count);
            _count++;
        }

        _samples[_next] = milliseconds;
        _next = (_next + 1) % _samples.Length;
        _total += milliseconds;
    }

    public FrameTimingSnapshot Snapshot()
    {
        if (_count == 0) return FrameTimingSnapshot.Empty;

        var last = _samples[(_next - 1 + _samples.Length) % _samples.Length];
        return new FrameTimingSnapshot(
            _count,
            last,
            _total / _count,
            Percentile(0.50),
            Percentile(0.95),
            _sorted[_count - 1]);
    }

    private void InsertSorted(double value, int length)
    {
        var insertion = Array.BinarySearch(_sorted, 0, length, value);
        if (insertion < 0) insertion = ~insertion;
        Array.Copy(_sorted, insertion, _sorted, insertion + 1, length - insertion);
        _sorted[insertion] = value;
    }

    private double Percentile(double percentile) =>
        _sorted[Math.Clamp((int)Math.Ceiling(_count * percentile) - 1, 0, _count - 1)];
}

internal readonly record struct ChunkPresentationProfileSnapshot(
    int ResidentSections,
    int ResidentSolidLayers,
    int ResidentTranslucentLayers,
    int VisibilityCandidates,
    int SpatialRegionTests,
    int SpatialColumnTests,
    int SpatialSectionTests,
    int SpatialFrustumCandidates,
    int SpatialCandidatesOutsideRenderDistance,
    int SpatialSortComparisons,
    int FrustumTests,
    int PortalVisited,
    int DisconnectedSeeds,
    int PortalQueuePops,
    int PortalDrawFrustumTests,
    int PortalEdgeAttempts,
    int PortalMissingNeighbors,
    int PortalMarginFrustumTests,
    int PortalMarginRejected,
    int PortalDuplicateReaches,
    int PortalSuccessfulReaches,
    int PortalMarginCacheHits,
    int SafetyRescued,
    int IncompleteAdjacencyRescued,
    int NewPresentationRescued,
    int PresentationRegressionRescued,
    int OldestSafetyRescueFrames,
    int PresentedSections,
    int PresentedSolidLayers,
    int PresentedTranslucentLayers,
    int EmptyLayersSubmitted,
    int TerrainDrawCalls,
    int AvailableQuads,
    int SubmittedQuads,
    int DirectionRejectedQuads,
    int DirectionDrawRanges,
    int UnassignedQuads,
    int TerrainUniformEntries,
    int TerrainSubmissionBatches,
    int TerrainStreamBinds,
    int TerrainPipelineBinds,
    int TerrainTextureBinds,
    int TerrainUniformArenaCapacity,
    int TerrainUniformArenaGrowths,
    long OpaqueBundleCandidateFrames,
    long OpaqueBundleReusableFrames,
    long OpaqueBundleInvalidations,
    long OpaqueBundleHits,
    long OpaqueBundleBuilds,
    long OpaqueBundleFallbackFrames,
    FrameTimingSnapshot FindVisible,
    FrameTimingSnapshot SpatialCull,
    FrameTimingSnapshot CandidateSort,
    FrameTimingSnapshot PortalTraversal,
    FrameTimingSnapshot TerrainSubmit);
