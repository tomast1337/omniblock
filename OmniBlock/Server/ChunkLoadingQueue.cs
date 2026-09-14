using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OmniBlock.Entities;
using OmniBlock.Server.Worlds;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Server;

internal readonly record struct ChunkGenerationPressure(
    int GameplayPending,
    int GameplayReady,
    int BackgroundQueued,
    int BackgroundRunning);

internal class ChunkLoadingQueue : IDisposable
{
    internal const int MaxChunkLoadWorkers = 8;
    internal const int MaxCompletedLoadsPerTick = 8;
    internal const double CompletedLoadBudgetMs = 4.0;

    private readonly ChunkMap _chunkMap;
    private readonly ConcurrentQueue<LoadedChunk> _completedChunks = [];

    // Every chunk requested but not yet applied. Player membership and publication stay on the
    // tick thread; coordinator workers only complete tasks into _completedChunks.
    private readonly Dictionary<long, PendingChunk> _inFlightChunks = [];
    private readonly ILogger<ChunkLoadingQueue> _logger = Log.Instance.For<ChunkLoadingQueue>();
    private readonly object _queueLock = new();

    // Workers finish in cost order, not distance order: a distant chunk found on disk can beat a
    // nearby chunk that needs generation. Keep completions here until the tick thread can publish
    // them as a near-to-far wavefront.
    private readonly List<LoadedChunk> _readyChunks = [];
    private readonly ThreadLocal<IChunkSource?> _workerGenerators = new();
    private readonly WorldGenerationCoordinator<Chunk> _generationCoordinator;
    private readonly WorldDecorationCoordinator _decorationCoordinator;
    private bool _disposed;
    private long _nextSequence;

    public ChunkLoadingQueue(ChunkMap chunkMap)
    {
        _chunkMap = chunkMap;

        // Chunk generation is CPU-heavy and this pool shares the process with eight client mesh
        // workers in single-player. Merely leaving two logical processors free still creates 30
        // loaders on a 32-thread machine, oversubscribing simulation, lighting, rendering and
        // networking. A bounded pool preserves parallel generation without flooding the scheduler.
        var workerCount = GetWorkerCount(Environment.ProcessorCount, chunkMap.SharesProcessWithClient);
        _generationCoordinator = new WorldGenerationCoordinator<Chunk>(workerCount, ProduceChunk);
        _decorationCoordinator = new WorldDecorationCoordinator(ProduceInactiveDecoration);
    }

    internal static int GetWorkerCount(int processorCount, bool sharesProcessWithClient = false) =>
        sharesProcessWithClient
            ? Math.Clamp((processorCount - 2) / 2, 1, MaxChunkLoadWorkers / 2)
            : Math.Clamp(processorCount - 2, 1, MaxChunkLoadWorkers);

    public void Add(int x, int z, ServerPlayerEntity player, bool relocationCritical = false)
    {
        // Spawn preparation may have populated the world cache before any per-player tracking
        // object exists. Adopt that resident chunk synchronously instead of starting a second
        // storage/generation job whose result would be discarded at publication.
        var chunkCache = _chunkMap.getWorld().ChunkCache;
        if (chunkCache.IsChunkLoaded(x, z))
        {
            var tracked = _chunkMap.GetOrCreateChunk(x, z, true)!;
            if (!tracked.HasPlayer(player)) tracked.addPlayer(player);
            return;
        }

        var hash = ChunkMap.GetChunkHash(x, z);

        lock (_queueLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inFlightChunks.TryGetValue(hash, out var existing))
            {
                existing.AddPlayer(player, relocationCritical, _generationCoordinator);
                return;
            }

            var pending = new PendingChunk(
                hash,
                x,
                z,
                _nextSequence++,
                CreateWorkKey(x, z));
            _inFlightChunks[hash] = pending;
            try
            {
                pending.AddPlayer(player, relocationCritical, _generationCoordinator);
            }
            catch
            {
                _inFlightChunks.Remove(hash);
                throw;
            }
            pending.GenerationCompletion.ContinueWith(
                task => OnGenerationCompleted(pending, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            UpdateTelemetryLocked();
        }
    }

    public void RemovePlayer(ServerPlayerEntity player)
    {
        lock (_queueLock)
        {
            var toDrop = new List<PendingChunk>();
            foreach (var chunk in _inFlightChunks.Values)
            {
                chunk.RemovePlayer(player);
                if (chunk.IsEmpty)
                {
                    toDrop.Add(chunk);
                }
            }

            foreach (var chunk in toDrop)
            {
                DropIfStillQueued(chunk);
            }

            UpdateTelemetryLocked();
        }
    }

    /// <summary>
    ///     Requests terrain for a non-gameplay owner without inserting it into the live cache.
    ///     If a player asks for the coordinate at the same source revision, both owners receive
    ///     the same produced chunk and only the player publication path activates it.
    /// </summary>
    internal WorldGenerationCoordinator<Chunk>.GenerationRequest<Chunk> RequestBackgroundTerrain(
        int x,
        int z,
        string owner,
        int radialDistance,
        long revision = 0) =>
        _generationCoordinator.Request(
            CreateWorkKey(x, z),
            owner,
            GenerationDesiredStage.Terrain,
            GenerationRequestPriority.BackgroundAt(radialDistance),
            revision);

    /// <summary>
    ///     Admits one isolated region-decoration transaction through the dimension's sole
    ///     decoration owner. The result remains inactive until its job durably saves it.
    /// </summary>
    internal WorldDecorationCoordinator.DecorationRequest RequestBackgroundDecoration(
        IEnumerable<ChunkPos> targets,
        string owner,
        int radialDistance) =>
        _decorationCoordinator.Request(
            targets,
            owner,
            GenerationRequestPriority.BackgroundAt(radialDistance));

    public void Remove(int x, int z, ServerPlayerEntity player)
    {
        var hash = ChunkMap.GetChunkHash(x, z);

        lock (_queueLock)
        {
            if (!_inFlightChunks.TryGetValue(hash, out var chunk))
            {
                return;
            }

            chunk.RemovePlayer(player);
            if (chunk.IsEmpty)
            {
                DropIfStillQueued(chunk);
            }

            UpdateTelemetryLocked();
        }
    }

    /// <summary>
    ///     Caller holds <see cref="_queueLock" />. Releasing the last player owner cancels queued
    ///     work immediately and makes an already-running result ineligible for publication.
    /// </summary>
    private void DropIfStillQueued(PendingChunk chunk)
    {
        _inFlightChunks.Remove(chunk.Hash);
        chunk.ReleaseGenerationRequests();
    }

    public void Tick() => ApplyCompletedLoads();

    public void ReprioritizeAll()
    {
        lock (_queueLock)
        {
            foreach (var pending in _inFlightChunks.Values)
                pending.UpdateGenerationDemand();
            UpdateTelemetryLocked();
        }
    }

    internal ChunkGenerationPressure SnapshotPressure()
    {
        lock (_queueLock)
        {
            var decoration = _decorationCoordinator.Snapshot();
            return new ChunkGenerationPressure(
                _inFlightChunks.Count,
                _readyChunks.Count + _completedChunks.Count,
                decoration.Queued,
                decoration.Running);
        }
    }

    internal bool IsPending(int chunkX, int chunkZ)
    {
        lock (_queueLock)
            return _inFlightChunks.ContainsKey(ChunkMap.GetChunkHash(chunkX, chunkZ));
    }

    /// <summary>Publishes a bounded batch of chunks completed by background workers.</summary>
    /// <remarks>
    ///     Publication is not a cheap queue drain: inserting a chunk initializes block light and
    ///     may decorate up to four neighbours. Letting a large worker burst run to completion here
    ///     makes simulation and the next chunk-send pass wait behind all of it. The count prevents
    ///     one timer anomaly from admitting an unbounded batch; the wall-clock budget adapts to
    ///     expensive chunks while always allowing at least one item to make progress.
    /// </remarks>
    private void ApplyCompletedLoads()
    {
        while (_completedChunks.TryDequeue(out var completed))
        {
            // Cancellation and task completion can race. Only a result whose exact request is
            // still owned by at least one player may enter the publication wavefront.
            if (_inFlightChunks.GetValueOrDefault(completed.Pending.Hash) == completed.Pending)
                _readyChunks.Add(completed);
        }
        UpdateTelemetry();

        var started = Stopwatch.GetTimestamp();
        var applied = 0;
        while (applied < MaxCompletedLoadsPerTick &&
               (applied == 0 || Stopwatch.GetElapsedTime(started).TotalMilliseconds < CompletedLoadBudgetMs) &&
               TryTakeNextPublishable(out var loaded))
        {
            var chunkToLoad = loaded.Pending;
            _inFlightChunks.Remove(chunkToLoad.Hash);

            if (loaded.Error is not null)
            {
                chunkToLoad.ReleaseGenerationRequests();
                // Removing the in-flight marker is the recovery mechanism. ChunkMap will request
                // this position again on a later tick if a player still needs it; retaining the
                // marker would make one failed generation attempt an invisible permanent hole.
                _logger.LogError(loaded.Error,
                    "Failed to load or generate chunk {ChunkX},{ChunkZ}; it may be retried",
                    chunkToLoad.X, chunkToLoad.Z);
                applied++;
                continue;
            }

            var players = chunkToLoad.Players.ToArray();
            chunkToLoad.ReleaseGenerationRequests();
            var chunkCache = _chunkMap.getWorld().ChunkCache;
            chunkCache.InsertLoadedChunk(chunkToLoad.X, chunkToLoad.Z, loaded.Chunk!);

            var chunk = _chunkMap.GetOrCreateChunk(chunkToLoad.X, chunkToLoad.Z, true);
            if (chunk != null)
            {
                foreach (var player in players)
                {
                    if (!chunk.HasPlayer(player))
                    {
                        chunk.addPlayer(player);
                    }
                }
            }
            applied++;
        }

        UpdateTelemetry();
    }

    /// <summary>
    ///     Takes the highest-priority completed chunk, but only after every closer ring has finished.
    /// </summary>
    /// <remarks>
    ///     Generation remains fully parallel. This gate controls only publication, preventing the
    ///     visible world from developing far-away islands while holes remain around the player.
    ///     Chunks in the same ring retain the movement-direction and age ordering.
    /// </remarks>
    private bool TryTakeNextPublishable(out LoadedChunk loaded)
    {
        loaded = null!;
        if (_readyChunks.Count == 0) return false;

        var bestReadyIndex = 0;
        var bestReadyPriority = _readyChunks[0].Pending.GetPriority();
        for (var i = 1; i < _readyChunks.Count; i++)
        {
            var priority = _readyChunks[i].Pending.GetPriority();
            if (priority.CompareTo(bestReadyPriority) >= 0) continue;
            bestReadyIndex = i;
            bestReadyPriority = priority;
        }

        var nearestOutstandingRing = int.MaxValue;
        foreach (var pending in _inFlightChunks.Values)
            nearestOutstandingRing = Math.Min(nearestOutstandingRing, pending.GetPriority().Ring);

        if (!CanPublishRing(bestReadyPriority.Ring, nearestOutstandingRing)) return false;

        loaded = _readyChunks[bestReadyIndex];
        _readyChunks.RemoveAt(bestReadyIndex);
        return true;
    }

    internal static bool CanPublishRing(int completedRing, int nearestOutstandingRing) =>
        completedRing <= nearestOutstandingRing;

    private Chunk ProduceChunk(GenerationWorkContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Resolved on first use rather than in ChunkMap's constructor, which runs before the
        // server has populated its world array. Coordinator workers are stable, so each keeps a
        // parallel-safe generator instance exactly as the former private loader pool did.
        var chunkCache = _chunkMap.getWorld().ChunkCache;
        var generator = _workerGenerators.Value ??= chunkCache.CreateParallelGenerator();
        try
        {
            var chunk = chunkCache.LoadOrGenerateChunkOffThread(
                context.Key.ChunkX,
                context.Key.ChunkZ,
                generator);
            cancellationToken.ThrowIfCancellationRequested();
            return chunk;
        }
        catch
        {
            _workerGenerators.Value = null;
            throw;
        }
    }

    private InactiveGenerationBatch ProduceInactiveDecoration(
        IReadOnlyList<ChunkPos> targets,
        CancellationToken cancellationToken) =>
        new InactiveGenerationWorkspace(_chunkMap.getWorld())
            .GenerateCompletedRegion(targets, cancellationToken);

    private void OnGenerationCompleted(PendingChunk pending, Task<Chunk> task)
    {
        if (task.IsCanceled) return;
        _completedChunks.Enqueue(task.IsFaulted
            ? new LoadedChunk(pending, null, task.Exception?.GetBaseException() ??
                new InvalidOperationException("Chunk generation failed without an exception."))
            : new LoadedChunk(pending, task.Result, null));
    }

    public void Dispose()
    {
        lock (_queueLock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var pending in _inFlightChunks.Values)
                pending.ReleaseGenerationRequests();
            _inFlightChunks.Clear();
        }
        _decorationCoordinator.Dispose();
        _generationCoordinator.Dispose();
        _workerGenerators.Dispose();
    }

    private static GenerationRequestPriority ToGenerationPriority(ChunkPriority priority) =>
        priority.Ring < 0
            ? GenerationRequestPriority.RelocationCritical
            : GenerationRequestPriority.GameplayAt(priority.Ring, priority.DirectionPenalty);

    private GenerationWorkKey CreateWorkKey(int x, int z) => new(
        _chunkMap.getWorld().Properties.LevelName,
        _chunkMap.DimensionId,
        x,
        z);

    private void UpdateTelemetry()
    {
        lock (_queueLock)
            UpdateTelemetryLocked();
    }

    private void UpdateTelemetryLocked()
    {
        var ready = _readyChunks.Count + _completedChunks.Count;
        var coordinator = _generationCoordinator.Snapshot();
        _chunkMap.getWorld().ChunkCache.GenerationTelemetry.SetQueueDepths(
            coordinator.Queued,
            coordinator.Running,
            ready);
    }

    private sealed record LoadedChunk(PendingChunk Pending, Chunk? Chunk, Exception? Error);

    private class PendingChunk
    {
        private readonly Dictionary<ServerPlayerEntity,
            WorldGenerationCoordinator<Chunk>.GenerationRequest<Chunk>> _generationRequests = [];
        private readonly HashSet<ServerPlayerEntity> _relocationOwners = [];

        public PendingChunk(long hash, int x, int z, long sequence, GenerationWorkKey workKey)
        {
            Hash = hash;
            X = x;
            Z = z;
            ChunkPos = new ChunkPos(x, z);
            Sequence = sequence;
            WorkKey = workKey;
        }

        public long Hash { get; }
        public int X { get; }
        public int Z { get; }
        public ChunkPos ChunkPos { get; }
        public IEnumerable<ServerPlayerEntity> Players => _generationRequests.Keys;
        public long Sequence { get; }
        public GenerationWorkKey WorkKey { get; }
        public bool IsEmpty => _generationRequests.Count == 0;
        public Task<Chunk> GenerationCompletion => _generationRequests.Values.FirstOrDefault()?.Completion ??
            throw new InvalidOperationException("Generation request has no player owner.");

        public void AddPlayer(
            ServerPlayerEntity player,
            bool relocationCritical,
            WorldGenerationCoordinator<Chunk> coordinator)
        {
            if (_generationRequests.TryGetValue(player, out var existing))
            {
                if (relocationCritical) _relocationOwners.Add(player);
                existing.UpdateDemand(GenerationDesiredStage.Terrain, GetPlayerPriority(player));
                return;
            }

            if (relocationCritical) _relocationOwners.Add(player);
            _generationRequests.Add(player, coordinator.Request(
                WorkKey,
                $"player:{player.ID}",
                GenerationDesiredStage.Terrain,
                GetPlayerPriority(player),
                revision: 0));
        }

        public void RemovePlayer(ServerPlayerEntity player)
        {
            _relocationOwners.Remove(player);
            if (!_generationRequests.Remove(player, out var request)) return;
            request.Dispose();
        }

        public void UpdateGenerationDemand()
        {
            foreach (var (player, request) in _generationRequests)
                request.UpdateDemand(GenerationDesiredStage.Terrain, GetPlayerPriority(player));
        }

        public void ReleaseGenerationRequests()
        {
            foreach (var request in _generationRequests.Values) request.Dispose();
            _generationRequests.Clear();
            _relocationOwners.Clear();
        }

        public ChunkPriority GetPriority()
        {
            var hasAny = false;
            ChunkPriority bestPriority = default;

            foreach (var player in _generationRequests.Keys)
            {
                var candidate = GetPlayerChunkPriority(player);
                if (!hasAny || candidate.CompareTo(bestPriority) < 0)
                {
                    bestPriority = candidate;
                    hasAny = true;
                }
            }

            return hasAny
                ? bestPriority
                : new ChunkPriority(int.MaxValue, double.MaxValue, Sequence);
        }

        private GenerationRequestPriority GetPlayerPriority(ServerPlayerEntity player) =>
            ToGenerationPriority(GetPlayerChunkPriority(player));

        private ChunkPriority GetPlayerChunkPriority(ServerPlayerEntity player) =>
            _relocationOwners.Contains(player)
                ? new ChunkPriority(-1, 0, Sequence)
                : player.GetChunkPriority(ChunkPos, Sequence);
    }
}
