using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.PathFinding;

/// <summary>
/// Per-world owner of AI path requests (EntityCreature/EntityWolf), separate from the plain
/// PathFinder NaturalSpawner uses directly and synchronously. RequestPath just queues; results
/// get pushed onto the entity (via setPathToEntity) once a batch completes, rather than requiring
/// the caller to poll for them — a caller that only requests occasionally (e.g. the ~1/80-chance
/// wander target) would otherwise rarely be the one to collect its own result. RunBatch processes
/// everything queued since the last call. See docs/parallel-pathfinding.md.
/// </summary>
internal sealed class PathingCoordinator(IWorldContext world)
{
    private readonly PathFinder _finder = new(world);
    private readonly List<PathRequest> _pendingRequests = [];

    internal void RequestPath(Entity entity, Entity target, float range) =>
        _pendingRequests.Add(new PathRequest(entity, target.X, target.BoundingBox.MinY, target.Z, range));

    internal void RequestPath(Entity entity, int x, int y, int z, float range) =>
        _pendingRequests.Add(new PathRequest(entity, x + 0.5, y + 0.5, z + 0.5, range));

    /// <summary>Runs every request queued since the last call. Call once per world tick, after all entities have ticked.</summary>
    internal void RunBatch()
    {
        foreach (PathRequest request in _pendingRequests)
        {
            PathEntity? path = _finder.CreateEntityPathTo(request.Entity, request.TargetX, request.TargetY, request.TargetZ, request.Range);
            if (request.Entity is EntityCreature creature)
            {
                creature.setPathToEntity(path);
            }
        }

        _pendingRequests.Clear();
    }
}

internal readonly record struct PathRequest(Entity Entity, double TargetX, double TargetY, double TargetZ, float Range);
