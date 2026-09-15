using System.Runtime.InteropServices;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Occlusion;

public interface IChunkVisibilityVisitor
{
    void Visit(SubChunkRenderer renderer);
}

public readonly record struct ChunkVisibilityResult(
    int ResidentCandidates,
    int FrustumTests,
    int FrustumCandidates,
    int PortalVisited,
    int DisconnectedSeeds = 0,
    int PortalQueuePops = 0,
    int PortalDrawFrustumTests = 0,
    int PortalEdgeAttempts = 0,
    int PortalMissingNeighbors = 0,
    int PortalMarginFrustumTests = 0,
    int PortalMarginRejected = 0,
    int PortalDuplicateReaches = 0,
    int PortalSuccessfulReaches = 0,
    int PortalMarginCacheHits = 0);

/// <summary>
///     Render-thread section visibility graph. Each incoming face is propagated exactly once and
///     multiple arrivals coalesce into one queued node. Exact and expanded frustum classifications
///     are cached for the duration of the search.
/// </summary>
public sealed class SectionVisibilityGraph
{
    private const double TraversalMargin = SubChunkRenderer.Size;
    private readonly Queue<SubChunkRenderer> _queue = new();
    private readonly Dictionary<SubChunkRenderer, TraversalState> _states = [];

    public ChunkVisibilityResult FindVisible(
        IChunkVisibilityVisitor visitor,
        IEnumerable<SubChunkRenderer> renderers,
        SubChunkRenderer? startNode,
        Vector3D<double> viewPos,
        ICuller culler,
        float renderDistance,
        bool useOcclusionCulling,
        int frame,
        bool candidatesKnownInFrustum = false)
    {
        _queue.Clear();
        _states.Clear();

        var residentCandidates = 0;
        var frustumTests = 0;
        var frustumCount = 0;
        var disconnectedSeeds = 0;
        var portalVisited = 0;
        var portalQueuePops = 0;
        var portalDrawFrustumTests = 0;
        var portalEdgeAttempts = 0;
        var portalMissingNeighbors = 0;
        var portalMarginFrustumTests = 0;
        var portalMarginRejected = 0;
        var portalDuplicateReaches = 0;
        var portalSuccessfulReaches = 0;
        var portalMarginCacheHits = 0;

        if (!useOcclusionCulling || startNode == null)
        {
            foreach (var renderer in renderers)
            {
                residentCandidates++;
                if (!candidatesKnownInFrustum && !TestFrustum(renderer.BoundingBox)) continue;
                frustumCount++;
                if (renderer.LastVisibleFrame == frame ||
                    !renderer.IsWithinRenderDistance(viewPos, renderDistance)) continue;
                renderer.LastVisibleFrame = frame;
                visitor.Visit(renderer);
            }

            return new ChunkVisibilityResult(
                residentCandidates, frustumTests, frustumCount, 0);
        }

        AddReach(startNode, ChunkDirectionMask.All);
        foreach (var renderer in renderers)
        {
            residentCandidates++;
            if (candidatesKnownInFrustum)
            {
                MarkExactFrustum(renderer, visible: true);
            }
            else if (!IsInExactFrustum(renderer))
            {
                continue;
            }

            frustumCount++;
            var unknown = ChunkDirectionMask.None;
            if (renderer.AdjacentDown == null) unknown |= ChunkDirectionMask.Down;
            if (renderer.AdjacentUp == null) unknown |= ChunkDirectionMask.Up;
            if (renderer.AdjacentNorth == null) unknown |= ChunkDirectionMask.North;
            if (renderer.AdjacentSouth == null) unknown |= ChunkDirectionMask.South;
            if (renderer.AdjacentWest == null) unknown |= ChunkDirectionMask.West;
            if (renderer.AdjacentEast == null) unknown |= ChunkDirectionMask.East;
            if (AddReach(renderer, unknown)) disconnectedSeeds++;
        }

        while (_queue.TryDequeue(out var current))
        {
            portalQueuePops++;
            ref var state = ref StateFor(current);
            state.Queued = false;
            var incomingDelta = state.Reached & ~state.Processed;
            if (incomingDelta == ChunkDirectionMask.None) continue;
            state.Processed |= incomingDelta;

            DrawIfVisible(current);
            var outgoing = current.VisibilityData.GetVisibleFrom(incomingDelta, viewPos, current);
            if ((outgoing & ChunkDirectionMask.Down) != 0)
                ReachThroughMargin(current.AdjacentDown, ChunkDirectionMask.Up);
            if ((outgoing & ChunkDirectionMask.Up) != 0)
                ReachThroughMargin(current.AdjacentUp, ChunkDirectionMask.Down);
            if ((outgoing & ChunkDirectionMask.North) != 0)
                ReachThroughMargin(current.AdjacentNorth, ChunkDirectionMask.South);
            if ((outgoing & ChunkDirectionMask.South) != 0)
                ReachThroughMargin(current.AdjacentSouth, ChunkDirectionMask.North);
            if ((outgoing & ChunkDirectionMask.West) != 0)
                ReachThroughMargin(current.AdjacentWest, ChunkDirectionMask.East);
            if ((outgoing & ChunkDirectionMask.East) != 0)
                ReachThroughMargin(current.AdjacentEast, ChunkDirectionMask.West);
        }

        return new ChunkVisibilityResult(
            residentCandidates,
            frustumTests,
            frustumCount,
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
            portalMarginCacheHits);

        void DrawIfVisible(SubChunkRenderer renderer)
        {
            if (renderer.LastVisibleFrame == frame || !IsInExactFrustum(renderer) ||
                !renderer.IsWithinRenderDistance(viewPos, renderDistance)) return;
            renderer.LastVisibleFrame = frame;
            visitor.Visit(renderer);
        }

        bool IsInExactFrustum(SubChunkRenderer renderer)
        {
            ref var state = ref StateFor(renderer);
            if (state.ExactFrustum != FrustumClassification.Unknown)
                return state.ExactFrustum == FrustumClassification.Visible;
            portalDrawFrustumTests++;
            state.ExactFrustum = TestFrustum(renderer.BoundingBox)
                ? FrustumClassification.Visible
                : FrustumClassification.Hidden;
            return state.ExactFrustum == FrustumClassification.Visible;
        }

        void MarkExactFrustum(SubChunkRenderer renderer, bool visible)
        {
            ref var state = ref StateFor(renderer);
            state.ExactFrustum = visible
                ? FrustumClassification.Visible
                : FrustumClassification.Hidden;
        }

        void ReachThroughMargin(SubChunkRenderer? renderer, ChunkDirectionMask incoming)
        {
            portalEdgeAttempts++;
            if (renderer == null)
            {
                portalMissingNeighbors++;
                return;
            }

            ref var state = ref StateFor(renderer);
            if ((state.Reached & incoming) == incoming)
            {
                portalDuplicateReaches++;
                return;
            }

            if (state.MarginFrustum == FrustumClassification.Unknown)
            {
                portalMarginFrustumTests++;
                state.MarginFrustum = TestFrustum(renderer.BoundingBox.Expand(
                        TraversalMargin, TraversalMargin, TraversalMargin))
                    ? FrustumClassification.Visible
                    : FrustumClassification.Hidden;
            }
            else
            {
                portalMarginCacheHits++;
            }

            if (state.MarginFrustum != FrustumClassification.Visible)
            {
                portalMarginRejected++;
                return;
            }

            if (AddReach(renderer, incoming)) portalSuccessfulReaches++;
        }

        bool AddReach(SubChunkRenderer renderer, ChunkDirectionMask incoming)
        {
            if (incoming == ChunkDirectionMask.None) return false;
            ref var state = ref StateFor(renderer);
            var newDirections = incoming & ~state.Reached;
            if (newDirections == ChunkDirectionMask.None) return false;
            if (state.Reached == ChunkDirectionMask.None) portalVisited++;
            state.Reached |= newDirections;
            if (!state.Queued)
            {
                state.Queued = true;
                _queue.Enqueue(renderer);
            }
            return true;
        }

        bool TestFrustum(Box bounds)
        {
            frustumTests++;
            return culler.IsBoundingBoxInFrustum(bounds);
        }
    }

    private ref TraversalState StateFor(SubChunkRenderer renderer) =>
        ref CollectionsMarshal.GetValueRefOrAddDefault(_states, renderer, out _);

    private struct TraversalState
    {
        public ChunkDirectionMask Reached;
        public ChunkDirectionMask Processed;
        public bool Queued;
        public FrustumClassification ExactFrustum;
        public FrustumClassification MarginFrustum;
    }

    private enum FrustumClassification : byte
    {
        Unknown,
        Hidden,
        Visible
    }
}
