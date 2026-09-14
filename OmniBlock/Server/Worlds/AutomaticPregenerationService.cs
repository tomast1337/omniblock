using System.Diagnostics;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Server.Worlds;

public enum AutomaticPregenerationProfile
{
    Play,
    Preparation
}

public sealed record AutomaticPregenerationOptions(
    bool Enabled,
    int RadiusChunks,
    AutomaticPregenerationProfile Profile)
{
    public static AutomaticPregenerationOptions Disabled { get; } =
        new(false, 32, AutomaticPregenerationProfile.Play);
}

public sealed record AutomaticPregenerationPressure(
    int GameplayPending,
    int BackgroundQueued,
    int LightingPending,
    double ServerTickMs,
    double? IntegratedClientFrameMs,
    double MemoryLoadRatio,
    long DiskFreeBytes);

public sealed record AutomaticPregenerationSnapshot(
    int Dimension,
    AutomaticPregenerationOptions Options,
    int ActivePlayers,
    int TrackedCenters,
    long PreparedTargets,
    long SavedTargets,
    long SkippedTargets,
    long GameplayDeferredTargets,
    long WrittenChunks,
    long DiskBytes,
    long PeakRetainedBytes,
    ChunkPos? ActiveTarget,
    bool Throttled,
    string ThrottleReason,
    string? LastError,
    AutomaticPregenerationPressure Pressure);

internal readonly record struct AutomaticGenerationPlayer(
    int Id,
    int ChunkX,
    int ChunkZ,
    double VelocityX,
    double VelocityZ);

/// <summary>
///     Ephemeral moving-area controller. It owns no workers: every admitted target goes through
///     the dimension's existing serialized inactive-decoration coordinator and durable storage
///     transaction. Completed-coordinate memory is explicitly bounded.
/// </summary>
internal sealed class AutomaticPregenerationService : IDisposable
{
    private const int CompletedCoordinateCapacity = 65_536;
    private readonly int _dimension;
    private readonly Func<ChunkPos, int, CancellationToken,
        Task<FixedAreaPregenerationWorkResult>> _execute;
    private readonly Func<AutomaticPregenerationPressure> _getPressure;
    private readonly Func<ChunkPos, bool> _hasGameplayOwnership;
    private readonly Dictionary<int, PlayerCursor> _players = [];
    private readonly HashSet<ChunkPos> _recentlyCompleted = [];
    private readonly Queue<ChunkPos> _completedOrder = [];
    private AutomaticPregenerationOptions _options;
    private AutomaticPregenerationPressure _lastPressure = EmptyPressure;
    private Task<FixedAreaPregenerationWorkResult>? _activeTask;
    private CancellationTokenSource? _activeCancellation;
    private Candidate? _activeCandidate;
    private ChunkPos? _recoveryTarget;
    private bool _pressureThrottled;
    private bool _faulted;
    private bool _disposed;
    private int _ticksUntilAdmission;
    private long _preparedTargets;
    private long _savedTargets;
    private long _skippedTargets;
    private long _gameplayDeferredTargets;
    private long _writtenChunks;
    private long _diskBytes;
    private long _peakRetainedBytes;
    private string _throttleReason = "disabled";
    private string? _lastError;
    private AutomaticPregenerationSnapshot _publishedSnapshot = null!;

    public AutomaticPregenerationService(
        ServerWorld world,
        ChunkMap chunkMap,
        Func<AutomaticPregenerationPressure> getPressure)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(chunkMap);
        _dimension = world.Dimension.Id;
        _options = AutomaticPregenerationOptions.Disabled;
        _getPressure = getPressure ?? throw new ArgumentNullException(nameof(getPressure));
        _hasGameplayOwnership = chunkMap.HasGameplayOwnershipInDecorationHalo;
        var stateRoot = world.GetWorldStorage().GetWorldGenerationStateDirectory()
            ?? throw new NotSupportedException(
                "This world does not support persistent automatic-generation checkpoints.");
        var stateDirectory = new DirectoryInfo(Path.Combine(
            stateRoot.FullName,
            $"dimension-{_dimension.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        var storage = world.GetWorldStorage().GetChunkStorage(world.Dimension)
            ?? throw new NotSupportedException($"Dimension {_dimension} has no chunk storage.");
        var checkpointId = $"automatic-dimension-{_dimension}";
        var checkpoint = new InactiveGenerationCheckpointStore(stateDirectory, checkpointId).Recover();
        if (checkpoint.Status == InactiveGenerationRecoveryStatus.InterruptedBatch)
        {
            var targets = checkpoint.PendingBatch!.Targets;
            if (targets.Count != 1)
                throw new InvalidDataException(
                    $"Automatic generation checkpoint for dimension {_dimension} contains " +
                    $"{targets.Count} targets; expected one.");
            _recoveryTarget = targets[0].ToChunkPos();
        }

        _execute = (position, radialDistance, token) =>
            InactiveGenerationTargetExecutor.Execute(
                world,
                chunkMap,
                storage,
                stateDirectory,
                checkpointId,
                $"automatic:{_dimension}:{position.X}:{position.Z}",
                position,
                radialDistance,
                token);
        PublishSnapshot();
    }

    internal AutomaticPregenerationService(
        int dimension,
        AutomaticPregenerationOptions options,
        Func<ChunkPos, int, CancellationToken, Task<FixedAreaPregenerationWorkResult>> execute,
        Func<AutomaticPregenerationPressure> getPressure,
        Func<ChunkPos, bool>? hasGameplayOwnership = null)
    {
        _dimension = dimension;
        _options = Validate(options);
        _execute = execute;
        _getPressure = getPressure;
        _hasGameplayOwnership = hasGameplayOwnership ?? (_ => false);
        _throttleReason = options.Enabled ? "discovering moving target" : "disabled";
        PublishSnapshot();
    }

    public AutomaticPregenerationOptions Options => _options;
    public bool RunsWhileSimulationPaused =>
        _options.Enabled && _options.Profile == AutomaticPregenerationProfile.Preparation;

    public void Configure(AutomaticPregenerationOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _options = Validate(options);
        _faulted = false;
        _lastError = null;
        _pressureThrottled = false;
        _ticksUntilAdmission = 0;
        _throttleReason = options.Enabled ? "discovering moving target" : "disabled";
        foreach (var cursor in _players.Values) cursor.ResetForRadius();
        if (!options.Enabled) CancelActive();
        PublishSnapshot();
    }

    public void Tick(IReadOnlyList<AutomaticGenerationPlayer> players)
    {
        try
        {
            TickCore(players);
        }
        finally
        {
            PublishSnapshot();
        }
    }

    private void TickCore(IReadOnlyList<AutomaticGenerationPlayer> players)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UpdatePlayers(players);
        CompleteActiveWork();

        if (!_options.Enabled)
        {
            _throttleReason = "disabled";
            return;
        }
        if (_faulted)
        {
            _throttleReason = "failed; reconfigure to retry";
            return;
        }
        if (_players.Count == 0)
        {
            CancelActive();
            _throttleReason = "waiting for a player in this dimension";
            return;
        }

        CancelActiveOutsideCoverage();
        if (_activeTask is not null) return;

        _lastPressure = _getPressure();
        var pressureReason = EvaluatePressure(_options.Profile, _lastPressure, _pressureThrottled);
        if (pressureReason is not null)
        {
            _pressureThrottled = true;
            _throttleReason = pressureReason;
            return;
        }
        _pressureThrottled = false;

        if (_ticksUntilAdmission-- > 0)
        {
            _throttleReason = "paced by profile admission interval";
            return;
        }
        _ticksUntilAdmission = _options.Profile == AutomaticPregenerationProfile.Play ? 3 : 0;

        var candidate = TakeNextCandidate();
        if (candidate is null)
        {
            if (_recoveryTarget is { } recovery && _hasGameplayOwnership(recovery))
                _throttleReason = "waiting for gameplay ownership to leave interrupted target";
            else
                _throttleReason = "current moving area is prepared";
            return;
        }

        _activeCandidate = candidate;
        _activeCancellation = new CancellationTokenSource();
        _preparedTargets++;
        _throttleReason = "preparing dependency halo";
        try
        {
            _activeTask = _execute(
                candidate.Position,
                candidate.Ring,
                _activeCancellation.Token);
        }
        catch (Exception error)
        {
            Fail(error);
            ClearActive();
        }
    }

    public AutomaticPregenerationSnapshot Snapshot() => Volatile.Read(ref _publishedSnapshot);

    private AutomaticPregenerationSnapshot BuildSnapshot() => new(
        _dimension,
        _options,
        _players.Count,
        _players.Count,
        _preparedTargets,
        _savedTargets,
        _skippedTargets,
        _gameplayDeferredTargets,
        _writtenChunks,
        _diskBytes,
        _peakRetainedBytes,
        _activeCandidate?.Position,
        _pressureThrottled,
        _throttleReason,
        _lastError,
        _lastPressure);

    private void PublishSnapshot() => Volatile.Write(ref _publishedSnapshot, BuildSnapshot());

    private void UpdatePlayers(IReadOnlyList<AutomaticGenerationPlayer> players)
    {
        var limit = _options.Profile == AutomaticPregenerationProfile.Play ? 8 : 32;
        var desired = players.OrderBy(static player => player.Id).Take(limit).ToArray();
        var desiredIds = desired.Select(static player => player.Id).ToHashSet();
        foreach (var id in _players.Keys.Where(id => !desiredIds.Contains(id)).ToArray())
        {
            _players[id].Dispose();
            _players.Remove(id);
        }

        var hysteresis = Math.Clamp(_options.RadiusChunks / 4, 2, 16);
        foreach (var player in desired)
        {
            if (!_players.TryGetValue(player.Id, out var cursor))
            {
                _players.Add(player.Id, new PlayerCursor(player, _options.RadiusChunks));
                continue;
            }
            cursor.Update(player, _options.RadiusChunks, hysteresis);
        }
    }

    private void CompleteActiveWork()
    {
        if (_activeTask is not { IsCompleted: true } task) return;
        var candidate = _activeCandidate!;
        try
        {
            var result = task.GetAwaiter().GetResult();
            _peakRetainedBytes = Math.Max(_peakRetainedBytes, result.RetainedBytes);
            if (result.Retry)
            {
                RememberCompleted(candidate.Position);
                _gameplayDeferredTargets++;
                _throttleReason = "yielded to active or newer gameplay terrain";
                return;
            }

            RememberCompleted(candidate.Position);
            _recoveryTarget = null;
            if (result.Skipped) _skippedTargets++;
            else _savedTargets++;
            _writtenChunks = checked(_writtenChunks + result.WrittenChunks);
            _diskBytes = checked(_diskBytes + result.DiskBytes);
            _throttleReason = result.Skipped
                ? "target already complete"
                : "durably saved; discovering next target";
        }
        catch (OperationCanceledException) when (
            _activeCancellation?.IsCancellationRequested == true)
        {
            _throttleReason = "obsolete moving target canceled";
        }
        catch (Exception error)
        {
            Fail(error);
        }
        finally
        {
            ClearActive();
        }
    }

    private Candidate? TakeNextCandidate()
    {
        if (_recoveryTarget is { } recovery)
        {
            if (_hasGameplayOwnership(recovery)) return null;
            return new Candidate(recovery, 0, 0, int.MinValue);
        }
        while (true)
        {
            Candidate? best = null;
            PlayerCursor? owner = null;
            foreach (var cursor in _players.Values)
            {
                var candidate = cursor.Peek(_recentlyCompleted);
                if (candidate is null) continue;
                if (best is null || candidate.CompareTo(best) < 0)
                {
                    best = candidate;
                    owner = cursor;
                }
            }
            owner?.Consume();
            if (best is null || !_hasGameplayOwnership(best.Position)) return best;
            RememberCompleted(best.Position);
            _gameplayDeferredTargets++;
        }
    }

    private void CancelActiveOutsideCoverage()
    {
        if (_activeCandidate is not { } candidate || _activeTask is null) return;
        var radiusSquared = (long)_options.RadiusChunks * _options.RadiusChunks;
        if (_players.Values.Any(cursor =>
                cursor.DistanceSquared(candidate.Position) <= radiusSquared)) return;
        CancelActive();
    }

    private void RememberCompleted(ChunkPos position)
    {
        if (!_recentlyCompleted.Add(position)) return;
        _completedOrder.Enqueue(position);
        while (_completedOrder.Count > CompletedCoordinateCapacity)
            _recentlyCompleted.Remove(_completedOrder.Dequeue());
    }

    private void CancelActive() => _activeCancellation?.Cancel();

    private void ClearActive()
    {
        _activeCancellation?.Dispose();
        _activeCancellation = null;
        _activeTask = null;
        _activeCandidate = null;
    }

    private void Fail(Exception error)
    {
        _faulted = true;
        _lastError = error.GetBaseException().Message;
        _throttleReason = error.GetBaseException() is IOException
            ? "storage failure"
            : "generation failure";
    }

    private static AutomaticPregenerationOptions Validate(AutomaticPregenerationOptions options)
    {
        var maximum = options.Profile == AutomaticPregenerationProfile.Play ? 64 : 256;
        if (options.RadiusChunks < 4 || options.RadiusChunks > maximum)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"Automatic {options.Profile.ToString().ToLowerInvariant()} radius must be between 4 and {maximum} chunks.");
        return options;
    }

    internal static string? EvaluatePressure(
        AutomaticPregenerationProfile profile,
        AutomaticPregenerationPressure pressure,
        bool alreadyThrottled)
    {
        var gameplayLimit = profile == AutomaticPregenerationProfile.Play ? 0 : 8;
        var lightingLimit = alreadyThrottled ? 1_024 : 4_096;
        var tickLimit = profile == AutomaticPregenerationProfile.Play
            ? alreadyThrottled ? 30 : 40
            : alreadyThrottled ? 42 : 48;
        var frameLimit = profile == AutomaticPregenerationProfile.Play
            ? alreadyThrottled ? 20 : 25
            : alreadyThrottled ? 30 : 40;
        var memoryLimit = alreadyThrottled ? 0.75 : 0.85;
        var diskLimit = alreadyThrottled ? 1536L * 1024 * 1024 : 1024L * 1024 * 1024;

        if (pressure.GameplayPending > gameplayLimit)
            return $"player chunk backlog ({pressure.GameplayPending})";
        if (pressure.BackgroundQueued > 0)
            return $"another background transaction is queued ({pressure.BackgroundQueued})";
        if (pressure.LightingPending > lightingLimit)
            return $"lighting backlog ({pressure.LightingPending})";
        if (pressure.ServerTickMs > tickLimit)
            return $"server tick pressure ({pressure.ServerTickMs:F1} ms)";
        if (pressure.IntegratedClientFrameMs is { } frameMs && frameMs > frameLimit)
            return $"integrated client frame pressure ({frameMs:F1} ms)";
        if (pressure.MemoryLoadRatio > memoryLimit)
            return $"process memory pressure ({pressure.MemoryLoadRatio:P0})";
        if (pressure.DiskFreeBytes < diskLimit)
            return $"low disk space ({pressure.DiskFreeBytes / (1024 * 1024)} MiB free)";
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelActive();
        foreach (var cursor in _players.Values) cursor.Dispose();
        _players.Clear();
        try
        {
            _activeTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException error) when (error.InnerExceptions.All(static inner =>
                   inner is OperationCanceledException))
        {
        }
        ClearActive();
    }

    private static readonly AutomaticPregenerationPressure EmptyPressure =
        new(0, 0, 0, 0, null, 0, long.MaxValue);

    private sealed record Candidate(
        ChunkPos Position,
        int Ring,
        double MovementPenalty,
        int PlayerId) : IComparable<Candidate>
    {
        public int CompareTo(Candidate? other) => other is null
            ? -1
            : (Ring, MovementPenalty, PlayerId, Position.X, Position.Z).CompareTo(
                (other.Ring, other.MovementPenalty, other.PlayerId,
                    other.Position.X, other.Position.Z));
    }

    private sealed class PlayerCursor : IDisposable
    {
        private AutomaticGenerationPlayer _player;
        private int _anchorX;
        private int _anchorZ;
        private int _radius;
        private IEnumerator<ChunkPos>? _enumerator;
        private Candidate? _next;

        public PlayerCursor(AutomaticGenerationPlayer player, int radius)
        {
            _player = player;
            _anchorX = player.ChunkX;
            _anchorZ = player.ChunkZ;
            _radius = radius;
            ResetForRadius();
        }

        public void Update(AutomaticGenerationPlayer player, int radius, int hysteresis)
        {
            _player = player;
            var dx = player.ChunkX - _anchorX;
            var dz = player.ChunkZ - _anchorZ;
            if (_radius == radius && (long)dx * dx + (long)dz * dz < (long)hysteresis * hysteresis)
                return;
            _anchorX = player.ChunkX;
            _anchorZ = player.ChunkZ;
            _radius = radius;
            ResetForRadius();
        }

        public void ResetForRadius()
        {
            _enumerator?.Dispose();
            _enumerator = CircularChunkTraversal.Enumerate(
                _anchorX, _anchorZ, _radius).GetEnumerator();
            _next = null;
        }

        public Candidate? Peek(HashSet<ChunkPos> completed)
        {
            if (_next is not null && !completed.Contains(_next.Position)) return _next;
            _next = null;
            while (_enumerator!.MoveNext())
            {
                var position = _enumerator.Current;
                if (completed.Contains(position)) continue;
                var ring = CircularChunkTraversal.RingDistance(_anchorX, _anchorZ, position);
                _next = new Candidate(position, ring, MovementPenalty(position), _player.Id);
                return _next;
            }
            return null;
        }

        public void Consume() => _next = null;

        public long DistanceSquared(ChunkPos position)
        {
            var dx = position.X - _anchorX;
            var dz = position.Z - _anchorZ;
            return (long)dx * dx + (long)dz * dz;
        }

        private double MovementPenalty(ChunkPos position)
        {
            var speed = Math.Sqrt(_player.VelocityX * _player.VelocityX +
                                  _player.VelocityZ * _player.VelocityZ);
            var dx = position.X - _player.ChunkX;
            var dz = position.Z - _player.ChunkZ;
            var distance = Math.Sqrt((double)dx * dx + (double)dz * dz);
            if (speed < 0.001 || distance < 0.001) return 0;
            var alignment = (dx * _player.VelocityX + dz * _player.VelocityZ) /
                            (distance * speed);
            return -Math.Clamp(alignment, -1, 1) * 0.25;
        }

        public void Dispose() => _enumerator?.Dispose();
    }
}
