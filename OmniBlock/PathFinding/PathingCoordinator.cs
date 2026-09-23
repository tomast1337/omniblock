using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.PathFinding;

/// <summary>
///     Per-world owner of AI path requests (EntityCreature, FollowOwnerBehavior), separate from the plain
///     PathFinder NaturalSpawner uses directly and synchronously. RequestPath just queues; results
///     get pushed onto the entity (via setPathToEntity) once a batch completes, rather than requiring
///     the caller to poll for them — a caller that only requests occasionally (e.g. the ~1/80-chance
///     wander target) would otherwise rarely be the one to collect its own result. RunBatch processes
///     everything queued since the last call.
/// </summary>
internal sealed class PathingCoordinator(IWorldContext world)
{
    private readonly List<PathRequest> _pendingRequests = [];

    // One PathFinder per worker thread: PathFinder's open-list/point-pool state is mutable
    // and not reentrant, so threads can't share a single instance.
    private readonly ThreadLocal<PathFinder> _threadFinder = new(() => new PathFinder(world));

    internal void RequestPath(Entity entity, Entity target, float range) =>
        _pendingRequests.Add(new PathRequest(entity, target.X, target.BoundingBox.MinY, target.Z, range));

    internal void RequestPath(Entity entity, int x, int y, int z, float range) =>
        _pendingRequests.Add(new PathRequest(entity, x + 0.5, y + 0.5, z + 0.5, range));

    /// <summary>Runs every request queued since the last call. Call once per world tick, after all entities have ticked.</summary>
    internal void RunBatch()
    {
        if (_pendingRequests.Count == 0) return;

        PathRequest[] requests = [.. _pendingRequests];
        var results = new PathEntity?[requests.Length];

        Parallel.For(0, requests.Length, i =>
        {
            var finder = _threadFinder.Value!;
            ref readonly var request = ref requests[i];
            results[i] = finder.CreateEntityPathTo(request.Entity, request.TargetX, request.TargetY, request.TargetZ, request.Range);
        });

        // Apply serially: setPathToEntity just writes one field on the target entity,
        // not worth risking two requests racing the same entity in the same batch.
        for (var i = 0; i < requests.Length; i++)
        {
            if (requests[i].Entity is EntityCreature creature)
            {
                creature.setPathToEntity(results[i]);
            }
        }

        _pendingRequests.Clear();
    }
}

internal readonly record struct PathRequest(Entity Entity, double TargetX, double TargetY, double TargetZ, float Range);
