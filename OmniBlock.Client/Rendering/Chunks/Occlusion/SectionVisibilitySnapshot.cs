using System.Diagnostics;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Occlusion;

/// <summary>
///     Immutable identity for one presentation captured by a visibility build. Publication checks
///     all three fields before touching the live renderer, so an evicted/recreated section or a
///     newer mesh can never be hidden by a late worker result.
/// </summary>
internal readonly record struct VisibilitySectionIdentity(
    Vector3D<int> Position,
    long LifetimeId,
    long PresentationEpoch);

internal readonly record struct VisibilityCandidate(
    VisibilitySectionIdentity Identity,
    bool PortalVisible);

internal readonly record struct VisibilityViewKey(
    Vector3D<int> CameraSection,
    Vector3D<int> CameraPositionClass,
    int YawClass,
    int PitchClass,
    int CameraMode,
    Matrix4X4<float> Projection,
    int RenderDistance,
    bool OcclusionEnabled)
{
    private const float AngularClassDegrees = 1.0f;
    private const double PositionClassSize = 8.0;

    public static VisibilityViewKey Create(
        Vector3D<int> cameraSection,
        Vector3D<double> cameraPosition,
        float yawDegrees,
        float pitchDegrees,
        int cameraMode,
        Matrix4X4<float> projection,
        int renderDistance,
        bool occlusionEnabled) => new(
        cameraSection,
        new Vector3D<int>(
            (int)Math.Floor(cameraPosition.X / PositionClassSize),
            (int)Math.Floor(cameraPosition.Y / PositionClassSize),
            (int)Math.Floor(cameraPosition.Z / PositionClassSize)),
        QuantizeAngle(yawDegrees),
        QuantizeAngle(pitchDegrees),
        cameraMode,
        projection,
        renderDistance,
        occlusionEnabled);

    private static int QuantizeAngle(float degrees) =>
        (int)MathF.Floor(NormalizeAngle(degrees) / AngularClassDegrees);

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360.0f;
        if (degrees >= 180.0f) degrees -= 360.0f;
        if (degrees < -180.0f) degrees += 360.0f;
        return degrees;
    }
}

internal sealed record SectionVisibilityBuildResult(
    long WorldGeneration,
    long GraphEpoch,
    VisibilityViewKey View,
    VisibilityCandidate[] ConservativeCandidates,
    ChunkVisibilityResult Diagnostics,
    double WorkerMilliseconds);

/// <summary>
///     Read-only section graph copied at a render-thread publication boundary. Worker traversal
///     never reads adjacency, presentation, scratch stamps, or disposal state from live renderers.
/// </summary>
internal sealed class SectionVisibilitySnapshot
{
    // An eight-block position cell contributes at most ~14 blocks of diagonal displacement and a
    // one-degree view class contributes ~9 blocks at the maximum 512-block exact distance. Two
    // sections conservatively cover both plus camera effects; exact filtering still runs live.
    internal const double ReuseMargin = SubChunkRenderer.Size * 2.0;
    private const double TraversalMargin = SubChunkRenderer.Size;

    private readonly Node[] _nodes;
    private readonly Dictionary<Vector3D<int>, int> _indices;

    private SectionVisibilitySnapshot(
        long worldGeneration,
        long graphEpoch,
        Node[] nodes,
        Dictionary<Vector3D<int>, int> indices)
    {
        WorldGeneration = worldGeneration;
        GraphEpoch = graphEpoch;
        _nodes = nodes;
        _indices = indices;
    }

    public long WorldGeneration { get; }
    public long GraphEpoch { get; }
    public int Count => _nodes.Length;

    public static SectionVisibilitySnapshot Capture(
        IEnumerable<SectionRenderState> residentSections,
        long worldGeneration,
        long graphEpoch)
    {
        // Graph topology comes from coordinates, so worker traversal does not need a sorted copy.
        // Avoiding an O(n log n) render-thread sort is significant at view distance 32.
        var captured = residentSections
            .Where(static state => !state.IsDisposed && state.Renderer != null)
            .ToArray();
        var indices = new Dictionary<Vector3D<int>, int>(captured.Length);
        for (var i = 0; i < captured.Length; i++) indices.Add(captured[i].Position, i);

        var nodes = new Node[captured.Length];
        var step = SubChunkRenderer.Size;
        for (var i = 0; i < captured.Length; i++)
        {
            var state = captured[i];
            var renderer = state.Renderer!;
            var position = renderer.Position;
            nodes[i] = new Node(
                new VisibilitySectionIdentity(position, state.LifetimeId, renderer.PresentedEpoch),
                renderer.BoundingBox,
                renderer.PositionPlus,
                renderer.VisibilityData,
                Index(position + new Vector3D<int>(0, -step, 0)),
                Index(position + new Vector3D<int>(0, step, 0)),
                Index(position + new Vector3D<int>(0, 0, -step)),
                Index(position + new Vector3D<int>(0, 0, step)),
                Index(position + new Vector3D<int>(-step, 0, 0)),
                Index(position + new Vector3D<int>(step, 0, 0)));
        }

        return new SectionVisibilitySnapshot(worldGeneration, graphEpoch, nodes, indices);

        int Index(Vector3D<int> position) => indices.GetValueOrDefault(position, -1);
    }

    public SectionVisibilityBuildResult Build(
        VisibilityViewKey view,
        Vector3D<int>? startPosition,
        ImmutableFrustum frustum,
        Vector3D<double> viewPosition,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var reached = new ChunkDirectionMask[_nodes.Length];
        var processed = new ChunkDirectionMask[_nodes.Length];
        var queued = new bool[_nodes.Length];
        var exactFrustum = new FrustumClassification[_nodes.Length];
        var marginFrustum = new FrustumClassification[_nodes.Length];
        var emitted = new bool[_nodes.Length];
        var queue = new Queue<int>();
        var visible = new List<VisibilitySectionIdentity>();

        var frustumTests = 0;
        var frustumCandidates = 0;
        var portalVisited = 0;
        var disconnectedSeeds = 0;
        var portalQueuePops = 0;
        var portalDrawFrustumTests = 0;
        var portalEdgeAttempts = 0;
        var portalMissingNeighbors = 0;
        var portalMarginFrustumTests = 0;
        var portalMarginRejected = 0;
        var portalDuplicateReaches = 0;
        var portalSuccessfulReaches = 0;
        var portalMarginCacheHits = 0;
        var expandedDistance = view.RenderDistance * SubChunkRenderer.Size + ReuseMargin;
        var start = startPosition.HasValue && _indices.TryGetValue(startPosition.Value, out var found)
            ? found
            : -1;

        if (!view.OcclusionEnabled || start < 0)
        {
            for (var i = 0; i < _nodes.Length; i++)
            {
                if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!IsInExactFrustum(i) || !IsWithinExpandedDistance(_nodes[i])) continue;
                Emit(i);
            }
        }
        else
        {
            AddReach(start, ChunkDirectionMask.All);
            for (var i = 0; i < _nodes.Length; i++)
            {
                if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!IsInExactFrustum(i)) continue;
                frustumCandidates++;
                ref readonly var node = ref _nodes[i];
                var unknown = ChunkDirectionMask.None;
                if (node.Down < 0) unknown |= ChunkDirectionMask.Down;
                if (node.Up < 0) unknown |= ChunkDirectionMask.Up;
                if (node.North < 0) unknown |= ChunkDirectionMask.North;
                if (node.South < 0) unknown |= ChunkDirectionMask.South;
                if (node.West < 0) unknown |= ChunkDirectionMask.West;
                if (node.East < 0) unknown |= ChunkDirectionMask.East;
                if (AddReach(i, unknown)) disconnectedSeeds++;
            }

            while (queue.TryDequeue(out var current))
            {
                if ((portalQueuePops++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                queued[current] = false;
                var incomingDelta = reached[current] & ~processed[current];
                if (incomingDelta == ChunkDirectionMask.None) continue;
                processed[current] |= incomingDelta;

                if (IsInExactFrustum(current) && IsWithinExpandedDistance(_nodes[current])) Emit(current);
                var outgoing = _nodes[current].Visibility.GetVisibleFrom(incomingDelta);
                if ((outgoing & ChunkDirectionMask.Down) != 0)
                    ReachThroughMargin(_nodes[current].Down, ChunkDirectionMask.Up);
                if ((outgoing & ChunkDirectionMask.Up) != 0)
                    ReachThroughMargin(_nodes[current].Up, ChunkDirectionMask.Down);
                if ((outgoing & ChunkDirectionMask.North) != 0)
                    ReachThroughMargin(_nodes[current].North, ChunkDirectionMask.South);
                if ((outgoing & ChunkDirectionMask.South) != 0)
                    ReachThroughMargin(_nodes[current].South, ChunkDirectionMask.North);
                if ((outgoing & ChunkDirectionMask.West) != 0)
                    ReachThroughMargin(_nodes[current].West, ChunkDirectionMask.East);
                if ((outgoing & ChunkDirectionMask.East) != 0)
                    ReachThroughMargin(_nodes[current].East, ChunkDirectionMask.West);
            }
        }

        if (!view.OcclusionEnabled || start < 0) frustumCandidates = visible.Count;
        var conservativeCandidates = new List<VisibilityCandidate>(frustumCandidates);
        for (var i = 0; i < _nodes.Length; i++)
        {
            if (exactFrustum[i] == FrustumClassification.Visible && IsWithinExpandedDistance(_nodes[i]))
                conservativeCandidates.Add(new VisibilityCandidate(_nodes[i].Identity, emitted[i]));
        }

        return new SectionVisibilityBuildResult(
            WorldGeneration,
            GraphEpoch,
            view,
            conservativeCandidates.ToArray(),
            new ChunkVisibilityResult(
                _nodes.Length,
                frustumTests,
                frustumCandidates,
                portalVisited,
                disconnectedSeeds,
                portalQueuePops,
                portalDrawFrustumTests,
                portalEdgeAttempts,
                portalMissingNeighbors,
                portalMarginFrustumTests,
                portalMarginRejected,
                portalDuplicateReaches,
                portalSuccessfulReaches,
                portalMarginCacheHits),
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        bool IsInExactFrustum(int index)
        {
            if (exactFrustum[index] != FrustumClassification.Unknown)
                return exactFrustum[index] == FrustumClassification.Visible;
            portalDrawFrustumTests++;
            frustumTests++;
            exactFrustum[index] = frustum.IsBoundingBoxInFrustum(
                    _nodes[index].Bounds.Expand(ReuseMargin, ReuseMargin, ReuseMargin))
                ? FrustumClassification.Visible
                : FrustumClassification.Hidden;
            return exactFrustum[index] == FrustumClassification.Visible;
        }

        void ReachThroughMargin(int index, ChunkDirectionMask incoming)
        {
            portalEdgeAttempts++;
            if (index < 0)
            {
                portalMissingNeighbors++;
                return;
            }

            if ((reached[index] & incoming) == incoming)
            {
                portalDuplicateReaches++;
                return;
            }

            if (marginFrustum[index] == FrustumClassification.Unknown)
            {
                portalMarginFrustumTests++;
                frustumTests++;
                var margin = ReuseMargin + TraversalMargin;
                marginFrustum[index] = frustum.IsBoundingBoxInFrustum(
                        _nodes[index].Bounds.Expand(margin, margin, margin))
                    ? FrustumClassification.Visible
                    : FrustumClassification.Hidden;
            }
            else
            {
                portalMarginCacheHits++;
            }

            if (marginFrustum[index] != FrustumClassification.Visible)
            {
                portalMarginRejected++;
                return;
            }

            if (AddReach(index, incoming)) portalSuccessfulReaches++;
        }

        bool AddReach(int index, ChunkDirectionMask incoming)
        {
            if (index < 0 || incoming == ChunkDirectionMask.None) return false;
            var newDirections = incoming & ~reached[index];
            if (newDirections == ChunkDirectionMask.None) return false;
            if (reached[index] == ChunkDirectionMask.None) portalVisited++;
            reached[index] |= newDirections;
            if (!queued[index])
            {
                queued[index] = true;
                queue.Enqueue(index);
            }
            return true;
        }

        bool IsWithinExpandedDistance(in Node node)
        {
            var dx = node.Center.X - viewPosition.X;
            var dy = node.Center.Y - viewPosition.Y;
            var dz = node.Center.Z - viewPosition.Z;
            return dx * dx + dz * dz < expandedDistance * expandedDistance &&
                   Math.Abs(dy) < expandedDistance;
        }

        void Emit(int index)
        {
            if (emitted[index]) return;
            emitted[index] = true;
            visible.Add(_nodes[index].Identity);
        }
    }

    private readonly record struct Node(
        VisibilitySectionIdentity Identity,
        Box Bounds,
        Vector3D<int> Center,
        ChunkVisibilityStore Visibility,
        int Down,
        int Up,
        int North,
        int South,
        int West,
        int East);
}
