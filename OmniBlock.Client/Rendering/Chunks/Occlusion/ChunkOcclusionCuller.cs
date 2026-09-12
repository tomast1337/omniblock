using Silk.NET.Maths;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Occlusion;

public interface IChunkVisibilityVisitor
{
    void Visit(SubChunkRenderer renderer);
}

public readonly record struct ChunkVisibilityResult(
    int ResidentCandidates,
    int FrustumTests,
    int FrustumCandidates,
    int PortalVisited);

public class ChunkOcclusionCuller
{
    private const double TraversalMargin = SubChunkRenderer.Size;
    private readonly Queue<SubChunkRenderer> _queue = new();
    private readonly Dictionary<SubChunkRenderer, ChunkDirectionMask> _reached = new();

    public ChunkVisibilityResult FindVisible(
        IChunkVisibilityVisitor visitor,
        IEnumerable<SubChunkRenderer> renderers,
        SubChunkRenderer? startNode,
        Vector3D<double> viewPos,
        ICuller culler,
        float renderDistance,
        bool useOcclusionCulling,
        int frame)
    {
        var residentCandidates = 0;
        var frustumTests = 0;
        var frustumCount = 0;

        // A missing camera mesh is normal during streaming and when flying above the world.
        // With no reliable portal seed, conservatively draw the available meshes in view.
        if (!useOcclusionCulling || startNode == null)
        {
            foreach (var renderer in renderers)
            {
                residentCandidates++;
                if (!IsInFrustum(renderer.BoundingBox)) continue;
                frustumCount++;
                DrawIfVisible(renderer, true);
            }

            return new ChunkVisibilityResult(
                residentCandidates, frustumTests, frustumCount, 0);
        }

        Reach(startNode, ChunkDirectionMask.All);
        foreach (var renderer in renderers)
        {
            residentCandidates++;
            // Only an exposed mesh that can actually contribute to this frame needs to seed a
            // disconnected component. The old pass seeded every hole in the entire resident mesh
            // cache. At distance 32 that turns a visibility query into a graph walk over roughly
            // 34,000 sections every frame, including terrain behind the camera.
            if (!IsInFrustum(renderer.BoundingBox)) continue;
            frustumCount++;

            // An absent neighbor is unknown space, not an opaque wall. Seed its exposed face so
            // a hole in the mesh cache cannot hide an otherwise finished component of terrain.
            var unknown = ChunkDirectionMask.None;
            if (renderer.AdjacentDown == null) unknown |= ChunkDirectionMask.Down;
            if (renderer.AdjacentUp == null) unknown |= ChunkDirectionMask.Up;
            if (renderer.AdjacentNorth == null) unknown |= ChunkDirectionMask.North;
            if (renderer.AdjacentSouth == null) unknown |= ChunkDirectionMask.South;
            if (renderer.AdjacentWest == null) unknown |= ChunkDirectionMask.West;
            if (renderer.AdjacentEast == null) unknown |= ChunkDirectionMask.East;
            Reach(renderer, unknown);
        }

        while (_queue.TryDequeue(out var current))
        {
            DrawIfVisible(current, false);
            // Connectivity may briefly leave the exact draw frustum before turning back toward
            // visible terrain, but it must not wander across the entire resident radius. One
            // section of margin preserves those edge paths and bounds work to the camera region.
            var outgoing = current.VisibilityData.GetVisibleFrom(_reached[current], viewPos, current);
            if ((outgoing & ChunkDirectionMask.Down) != 0) ReachIfNearFrustum(current.AdjacentDown, ChunkDirectionMask.Up);
            if ((outgoing & ChunkDirectionMask.Up) != 0) ReachIfNearFrustum(current.AdjacentUp, ChunkDirectionMask.Down);
            if ((outgoing & ChunkDirectionMask.North) != 0) ReachIfNearFrustum(current.AdjacentNorth, ChunkDirectionMask.South);
            if ((outgoing & ChunkDirectionMask.South) != 0) ReachIfNearFrustum(current.AdjacentSouth, ChunkDirectionMask.North);
            if ((outgoing & ChunkDirectionMask.West) != 0) ReachIfNearFrustum(current.AdjacentWest, ChunkDirectionMask.East);
            if ((outgoing & ChunkDirectionMask.East) != 0) ReachIfNearFrustum(current.AdjacentEast, ChunkDirectionMask.West);
        }

        var portalVisited = _reached.Count;
        _reached.Clear();
        return new ChunkVisibilityResult(
            residentCandidates, frustumTests, frustumCount, portalVisited);

        void DrawIfVisible(SubChunkRenderer renderer, bool knownInFrustum)
        {
            if (renderer.LastVisibleFrame == frame ||
                !(knownInFrustum
                    ? renderer.IsWithinRenderDistance(viewPos, renderDistance)
                    : IsInFrustum(renderer.BoundingBox) &&
                      renderer.IsWithinRenderDistance(viewPos, renderDistance))) return;
            renderer.LastVisibleFrame = frame;
            visitor.Visit(renderer);
        }

        void ReachIfNearFrustum(SubChunkRenderer? renderer, ChunkDirectionMask incoming)
        {
            if (renderer == null ||
                !IsInFrustum(renderer.BoundingBox.Expand(
                    TraversalMargin, TraversalMargin, TraversalMargin))) return;
            Reach(renderer, incoming);
        }

        bool IsInFrustum(Box bounds)
        {
            frustumTests++;
            return culler.IsBoundingBoxInFrustum(bounds);
        }
    }

    private void Reach(SubChunkRenderer? renderer, ChunkDirectionMask incoming)
    {
        if (renderer == null || incoming == ChunkDirectionMask.None) return;
        _reached.TryGetValue(renderer, out var previous);
        if ((previous | incoming) == previous) return;
        _reached[renderer] = previous | incoming;
        // A second path may enter through a different face after the first path was processed.
        // Revisit on new incoming faces; there are at most six such changes per mesh.
        _queue.Enqueue(renderer);
    }
}
