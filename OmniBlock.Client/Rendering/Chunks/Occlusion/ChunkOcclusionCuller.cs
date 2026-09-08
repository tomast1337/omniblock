using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Occlusion;

public interface IChunkVisibilityVisitor
{
    void Visit(SubChunkRenderer renderer);
}

public class ChunkOcclusionCuller
{
    private readonly Queue<SubChunkRenderer> _queue = new();
    private readonly Dictionary<SubChunkRenderer, ChunkDirectionMask> _reached = new();

    public void FindVisible(
        IChunkVisibilityVisitor visitor,
        IEnumerable<SubChunkRenderer> renderers,
        SubChunkRenderer? startNode,
        Vector3D<double> viewPos,
        ICuller culler,
        float renderDistance,
        bool useOcclusionCulling,
        int frame)
    {
        // A missing camera mesh is normal during streaming and when flying above the world.
        // With no reliable portal seed, conservatively draw the available meshes in view.
        if (!useOcclusionCulling || startNode == null)
        {
            foreach (var renderer in renderers)
                DrawIfVisible(renderer);
            return;
        }

        Reach(startNode, ChunkDirectionMask.All);
        foreach (var renderer in renderers)
        {
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
            DrawIfVisible(current);
            // Frustum selection affects drawing only. A connected path can leave the frustum
            // or turn back toward the camera before reaching visible terrain.
            var outgoing = current.VisibilityData.GetVisibleFrom(_reached[current], viewPos, current);
            if ((outgoing & ChunkDirectionMask.Down) != 0) Reach(current.AdjacentDown, ChunkDirectionMask.Up);
            if ((outgoing & ChunkDirectionMask.Up) != 0) Reach(current.AdjacentUp, ChunkDirectionMask.Down);
            if ((outgoing & ChunkDirectionMask.North) != 0) Reach(current.AdjacentNorth, ChunkDirectionMask.South);
            if ((outgoing & ChunkDirectionMask.South) != 0) Reach(current.AdjacentSouth, ChunkDirectionMask.North);
            if ((outgoing & ChunkDirectionMask.West) != 0) Reach(current.AdjacentWest, ChunkDirectionMask.East);
            if ((outgoing & ChunkDirectionMask.East) != 0) Reach(current.AdjacentEast, ChunkDirectionMask.West);
        }

        _reached.Clear();

        void DrawIfVisible(SubChunkRenderer renderer)
        {
            if (renderer.LastVisibleFrame == frame || !renderer.IsVisible(culler, viewPos, renderDistance)) return;
            renderer.LastVisibleFrame = frame;
            visitor.Visit(renderer);
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
