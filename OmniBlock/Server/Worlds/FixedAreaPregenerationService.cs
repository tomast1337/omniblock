using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Server.Worlds;

public enum FixedAreaPregenerationStatus
{
    Paused,
    Running,
    Completed,
    Canceled,
    Failed
}

public sealed record FixedAreaPregenerationDefinition(
    string Id,
    string World,
    int Dimension,
    long Seed,
    string GeneratorProfile,
    string GeneratorOptionsHash,
    string ContentFingerprint,
    int CenterChunkX,
    int CenterChunkZ,
    int RadiusChunks,
    long TotalTargets,
    DateTimeOffset CreatedUtc);

public sealed record FixedAreaPregenerationSnapshot(
    FixedAreaPregenerationDefinition Definition,
    FixedAreaPregenerationStatus Status,
    long NextTarget,
    long PreparedTargets,
    long DecoratedTargets,
    long SavedTargets,
    long SkippedTargets,
    long WrittenChunks,
    long RetainedBytes,
    long PeakRetainedBytes,
    long DiskBytes,
    double TargetsPerSecond,
    string ThrottleReason,
    string? LastError,
    DateTimeOffset UpdatedUtc)
{
    public long RemainingTargets => Math.Max(0, Definition.TotalTargets - NextTarget);
}

internal sealed record FixedAreaPregenerationWorkResult(
    bool Skipped,
    bool Retry,
    int WrittenChunks,
    long RetainedBytes,
    long DiskBytes);

/// <summary>
///     Persistent fixed-area world preparation. Discovery is lazy and allocation-free with respect
///     to target area: at most one target transaction and its dependency halo are retained. Actual
///     generation is admitted by <see cref="WorldDecorationCoordinator"/>, so player demand keeps
///     the earlier coordinator priority and no second generation worker pool exists.
/// </summary>
internal sealed class FixedAreaPregenerationService : IDisposable
{
    private const int CurrentFormatVersion = 1;
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenDirectory = 0x10000;
    private readonly ILogger _logger = Log.Instance.For<FixedAreaPregenerationService>();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly DirectoryInfo _directory;
    private readonly Func<Job, ChunkPos, CancellationToken, Task<FixedAreaPregenerationWorkResult>> _execute;
    private Job? _activeJob;
    private Task<FixedAreaPregenerationWorkResult>? _activeWork;
    private CancellationTokenSource? _activeCancellation;
    private IReadOnlyList<FixedAreaPregenerationSnapshot> _publishedSnapshots =
        Array.Empty<FixedAreaPregenerationSnapshot>();
    private bool _disposed;

    public FixedAreaPregenerationService(ServerWorld world, ChunkMap chunkMap)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(chunkMap);
        var stateRoot = world.GetWorldStorage().GetWorldGenerationStateDirectory()
            ?? throw new NotSupportedException("This world does not support persistent pregeneration jobs.");
        _directory = new DirectoryInfo(Path.Combine(
            stateRoot.FullName,
            $"dimension-{world.Dimension.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        var storage = world.GetWorldStorage().GetChunkStorage(world.Dimension)
            ?? throw new NotSupportedException($"Dimension {world.Dimension.Id} has no chunk storage.");
        _execute = (job, position, token) =>
            Execute(world, chunkMap, storage, job, position, token);
        Load(world);
    }

    internal FixedAreaPregenerationService(
        DirectoryInfo directory,
        Func<FixedAreaPregenerationDefinition, ChunkPos, CancellationToken,
            Task<FixedAreaPregenerationWorkResult>> execute)
    {
        _directory = directory;
        _execute = (job, position, token) => execute(job.Record.Definition, position, token);
        Load(null);
    }

    public FixedAreaPregenerationSnapshot Start(
        string id,
        ServerWorld world,
        int centerChunkX,
        int centerChunkZ,
        int radiusChunks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateId(id);
        if (radiusChunks < 0 || radiusChunks > 4096)
            throw new ArgumentOutOfRangeException(nameof(radiusChunks),
                "Pregeneration radius must be between 0 and 4096 chunks.");
        if (_jobs.ContainsKey(id))
            throw new InvalidOperationException($"Pregeneration job '{id}' already exists.");

        var definition = CreateDefinition(id, world, centerChunkX, centerChunkZ, radiusChunks);
        var now = DateTimeOffset.UtcNow;
        var job = new Job(new PersistedJob(
            CurrentFormatVersion,
            definition,
            FixedAreaPregenerationStatus.Running,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            "discovering next radial target",
            null,
            now));
        _jobs.Add(id, job);
        try
        {
            Save(job);
        }
        catch
        {
            _jobs.Remove(id);
            throw;
        }
        return Snapshot(job);
    }

    internal FixedAreaPregenerationSnapshot Start(FixedAreaPregenerationDefinition definition)
    {
        ValidateId(definition.Id);
        if (definition.TotalTargets != CircularChunkTraversal.Count(definition.RadiusChunks))
            throw new InvalidDataException($"Pregeneration job '{definition.Id}' has an invalid target count.");
        if (_jobs.ContainsKey(definition.Id))
            throw new InvalidOperationException($"Pregeneration job '{definition.Id}' already exists.");
        var job = new Job(new PersistedJob(
            CurrentFormatVersion, definition, FixedAreaPregenerationStatus.Running,
            0, 0, 0, 0, 0, 0, 0, 0, 0, "discovering next radial target", null,
            DateTimeOffset.UtcNow));
        _jobs.Add(definition.Id, job);
        try
        {
            Save(job);
        }
        catch
        {
            _jobs.Remove(definition.Id);
            throw;
        }
        return Snapshot(job);
    }

    public FixedAreaPregenerationSnapshot Inspect(string id) => Snapshot(Get(id));

    public IReadOnlyList<FixedAreaPregenerationSnapshot> InspectAll() =>
        _jobs.Values.OrderBy(static job => job.Record.Definition.Id, StringComparer.Ordinal)
            .Select(Snapshot).ToArray();

    /// <summary>
    ///     Last durably published progress view. Unlike <see cref="InspectAll"/>, this can be read
    ///     from the integrated client's UI thread without touching the server-owned job table.
    /// </summary>
    public IReadOnlyList<FixedAreaPregenerationSnapshot> PublishedSnapshots =>
        Volatile.Read(ref _publishedSnapshots);

    public FixedAreaPregenerationSnapshot Pause(string id)
    {
        var job = Get(id);
        if (job.Record.Status == FixedAreaPregenerationStatus.Running)
        {
            job.Record = job.Record with
            {
                Status = FixedAreaPregenerationStatus.Paused,
                ThrottleReason = "paused by operator",
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            CancelActive(job);
            Save(job);
        }
        return Snapshot(job);
    }

    public FixedAreaPregenerationSnapshot Resume(string id)
    {
        var job = Get(id);
        if (job.Record.Status is FixedAreaPregenerationStatus.Completed or
            FixedAreaPregenerationStatus.Canceled)
            throw new InvalidOperationException(
                $"Pregeneration job '{id}' is {job.Record.Status.ToString().ToLowerInvariant()} and cannot resume.");
        job.Record = job.Record with
        {
            Status = FixedAreaPregenerationStatus.Running,
            ThrottleReason = "discovering next radial target",
            LastError = null,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        Save(job);
        return Snapshot(job);
    }

    public FixedAreaPregenerationSnapshot Cancel(string id)
    {
        var job = Get(id);
        if (job.Record.Status is not (FixedAreaPregenerationStatus.Completed or
            FixedAreaPregenerationStatus.Canceled))
        {
            job.Record = job.Record with
            {
                Status = FixedAreaPregenerationStatus.Canceled,
                ThrottleReason = "canceled; committed terrain retained",
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            CancelActive(job);
            Save(job);
        }
        return Snapshot(job);
    }

    /// <summary>Advances orchestration only; generation/decorating and durable I/O stay off-thread.</summary>
    public void Tick()
    {
        try
        {
            TickCore();
        }
        catch (Exception error)
        {
            var job = _activeJob ?? _jobs.Values.FirstOrDefault(static candidate =>
                candidate.Record.Status == FixedAreaPregenerationStatus.Running);
            if (job is not null) Fail(job, error);
            else _logger.LogError(error, "Pregeneration orchestration failed without an active job");
            ClearActive();
        }
    }

    private void TickCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeWork is { IsCompleted: true })
        {
            CompleteActiveWork();
            return;
        }
        if (_activeWork is not null) return;

        var job = _jobs.Values
            .Where(static candidate => candidate.Record.Status == FixedAreaPregenerationStatus.Running)
            .OrderBy(static candidate => candidate.Record.UpdatedUtc)
            .ThenBy(static candidate => candidate.Record.Definition.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (job is null) return;
        if (job.Record.NextTarget >= job.Record.Definition.TotalTargets)
        {
            job.Record = job.Record with
            {
                Status = FixedAreaPregenerationStatus.Completed,
                ThrottleReason = "complete",
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            Save(job);
            return;
        }

        var position = job.GetTarget();
        var recovery = new InactiveGenerationCheckpointStore(_directory, job.Record.Definition.Id).Recover();
        var retryInterrupted = recovery.Status == InactiveGenerationRecoveryStatus.InterruptedBatch;

        _activeJob = job;
        _activeCancellation = new CancellationTokenSource();
        job.Record = job.Record with
        {
            PreparedTargets = checked(job.Record.PreparedTargets + 1),
            ThrottleReason = retryInterrupted
                ? "retrying interrupted durable batch"
                : "preparing dependency halo",
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        Save(job);
        try
        {
            _activeWork = _execute(job, position, _activeCancellation.Token);
        }
        catch (Exception error)
        {
            Fail(job, error);
            ClearActive();
        }
    }

    private async Task<FixedAreaPregenerationWorkResult> Execute(
        ServerWorld world,
        ChunkMap chunkMap,
        IChunkStorage storage,
        Job job,
        ChunkPos position,
        CancellationToken cancellationToken) =>
        await InactiveGenerationTargetExecutor.Execute(
            world,
            chunkMap,
            storage,
            _directory,
            job.Record.Definition.Id,
            $"pregen:{job.Record.Definition.Id}",
            position,
            CircularChunkTraversal.RingDistance(
                job.Record.Definition.CenterChunkX,
                job.Record.Definition.CenterChunkZ,
                position),
            cancellationToken).ConfigureAwait(false);

    private void CompleteActiveWork()
    {
        var job = _activeJob!;
        var task = _activeWork!;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (job.Record.Status == FixedAreaPregenerationStatus.Running)
            {
                if (result.Retry)
                {
                    job.Record = job.Record with
                    {
                        RetainedBytes = 0,
                        PeakRetainedBytes = Math.Max(
                            job.Record.PeakRetainedBytes, result.RetainedBytes),
                        ThrottleReason = "yielded to newer saved terrain; retrying target",
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    Save(job);
                    return;
                }

                job.Record = job.Record with
                {
                    NextTarget = checked(job.Record.NextTarget + 1),
                    DecoratedTargets = result.Skipped
                        ? job.Record.DecoratedTargets
                        : checked(job.Record.DecoratedTargets + 1),
                    SavedTargets = result.Skipped
                        ? job.Record.SavedTargets
                        : checked(job.Record.SavedTargets + 1),
                    SkippedTargets = result.Skipped
                        ? checked(job.Record.SkippedTargets + 1)
                        : job.Record.SkippedTargets,
                    WrittenChunks = checked(job.Record.WrittenChunks + result.WrittenChunks),
                    RetainedBytes = 0,
                    PeakRetainedBytes = Math.Max(
                        job.Record.PeakRetainedBytes, result.RetainedBytes),
                    DiskBytes = checked(job.Record.DiskBytes + result.DiskBytes),
                    ThrottleReason = result.Skipped
                        ? "target already complete; dependency halo saved"
                        : "durably saved; discovering next target",
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                Save(job);
            }
        }
        catch (OperationCanceledException) when (
            job.Record.Status is FixedAreaPregenerationStatus.Paused or
                FixedAreaPregenerationStatus.Canceled)
        {
            // The operator state was persisted when cancellation was requested.
        }
        catch (Exception error)
        {
            Fail(job, error);
        }
        finally
        {
            ClearActive();
        }
    }

    private void Fail(Job job, Exception error)
    {
        _logger.LogError(error, "Pregeneration job {JobId} failed", job.Record.Definition.Id);
        job.Record = job.Record with
        {
            Status = FixedAreaPregenerationStatus.Failed,
            LastError = error.GetBaseException().Message,
            ThrottleReason = ClassifyFailure(error),
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        try
        {
            Save(job);
        }
        catch (Exception persistenceError)
        {
            _logger.LogError(persistenceError,
                "Could not persist failed state for pregeneration job {JobId}",
                job.Record.Definition.Id);
        }
        finally
        {
            PublishSnapshots();
        }
    }

    private static string ClassifyFailure(Exception error) => error.GetBaseException() switch
    {
        IOException io when io.HResult == unchecked((int)0x80070070) => "disk full",
        IOException => "storage failure",
        _ => "generation failure"
    };

    private void CancelActive(Job job)
    {
        if (ReferenceEquals(_activeJob, job)) _activeCancellation?.Cancel();
    }

    private void ClearActive()
    {
        _activeCancellation?.Dispose();
        _activeCancellation = null;
        _activeWork = null;
        _activeJob = null;
    }

    private void Load(ServerWorld? world)
    {
        if (!_directory.Exists) return;
        foreach (var path in _directory.EnumerateFiles("*.pregen.json"))
        {
            PersistedJob record;
            try
            {
                using var stream = path.OpenRead();
                record = JsonSerializer.Deserialize<PersistedJob>(stream, JsonOptions)
                    ?? throw new InvalidDataException($"Pregeneration metadata '{path.FullName}' is empty.");
                if (world is not null) Validate(record, world);
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Could not load pregeneration job metadata {Path}", path.FullName);
                continue;
            }

            var checkpoint = new InactiveGenerationCheckpointStore(_directory, record.Definition.Id).Recover();
            var committed = checkpoint.Checkpoint.CommittedTargets;
            var acknowledged = checked(record.SavedTargets + record.SkippedTargets);
            if (committed > acknowledged)
            {
                var missingAcknowledgements = committed - acknowledged;
                record = record with
                {
                    NextTarget = Math.Min(record.Definition.TotalTargets,
                        checked(record.NextTarget + missingAcknowledgements)),
                    DecoratedTargets = checked(record.DecoratedTargets + missingAcknowledgements),
                    SavedTargets = checked(record.SavedTargets + missingAcknowledgements),
                    WrittenChunks = checkpoint.Checkpoint.WrittenChunks,
                    DiskBytes = checkpoint.Checkpoint.SizeDeltaBytes
                };
            }
            if (record.Status == FixedAreaPregenerationStatus.Running)
                record = record with
                {
                    Status = FixedAreaPregenerationStatus.Paused,
                    ThrottleReason = "paused after server restart; resume explicitly",
                    LastError = checkpoint.Status == InactiveGenerationRecoveryStatus.InterruptedBatch
                        ? "An interrupted durable batch will be retried on resume."
                        : record.LastError,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            var job = new Job(record);
            _jobs.Add(record.Definition.Id, job);
            Save(job);
        }
    }

    private static void Validate(PersistedJob record, ServerWorld world)
    {
        if (record.FormatVersion != CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported pregeneration job format {record.FormatVersion}.");
        var expected = CreateDefinition(record.Definition.Id, world,
            record.Definition.CenterChunkX, record.Definition.CenterChunkZ,
            record.Definition.RadiusChunks) with
        {
            CreatedUtc = record.Definition.CreatedUtc
        };
        if (record.Definition != expected)
            throw new InvalidDataException(
                $"Pregeneration job '{record.Definition.Id}' does not match this world, generator, or content catalog.");
        if (record.NextTarget < 0 || record.NextTarget > record.Definition.TotalTargets)
            throw new InvalidDataException($"Pregeneration job '{record.Definition.Id}' has an invalid cursor.");
    }

    private static FixedAreaPregenerationDefinition CreateDefinition(
        string id, ServerWorld world, int centerChunkX, int centerChunkZ, int radiusChunks) =>
        new(
            id,
            world.Properties.LevelName,
            world.Dimension.Id,
            world.Seed,
            world.Dimension.Id == -1
                ? world.Content.DimensionGeneratorProfiles.GetByDimensionId(-1).GeneratorProviderType.ToString()
                : world.Properties.TerrainType.Key.ToString(),
            Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes(world.Properties.GeneratorOptions ?? string.Empty))),
            world.Content.Manifest.Fingerprint,
            centerChunkX,
            centerChunkZ,
            radiusChunks,
            CircularChunkTraversal.Count(radiusChunks),
            DateTimeOffset.UtcNow);

    private void Save(Job job)
    {
        _directory.Create();
        var stem = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(job.Record.Definition.Id)));
        var path = Path.Combine(_directory.FullName, $"{stem}.pregen.json");
        var temporary = $"{path}.tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                   FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, job.Record, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
        FlushDirectory();
        PublishSnapshots();
    }

    private void PublishSnapshots() => Volatile.Write(ref _publishedSnapshots,
        Array.AsReadOnly(_jobs.Values
            .OrderBy(static job => job.Record.Definition.Id, StringComparer.Ordinal)
            .Select(Snapshot).ToArray()));

    private void FlushDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;
        var descriptor = LinuxOpen(_directory.FullName, LinuxOpenReadOnly | LinuxOpenDirectory);
        if (descriptor < 0)
            throw new IOException(
                $"Could not open pregeneration directory '{_directory.FullName}' for durable flush.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (LinuxFsync(descriptor) != 0)
                throw new IOException(
                    $"Could not durably flush pregeneration directory '{_directory.FullName}'.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally
        {
            LinuxClose(descriptor);
        }
    }

    private FixedAreaPregenerationSnapshot Snapshot(Job job)
    {
        var record = job.Record;
        var elapsed = Math.Max(0.001, Stopwatch.GetElapsedTime(job.StartedAt).TotalSeconds);
        return new FixedAreaPregenerationSnapshot(
            record.Definition, record.Status, record.NextTarget, record.PreparedTargets,
            record.DecoratedTargets, record.SavedTargets, record.SkippedTargets,
            record.WrittenChunks, record.RetainedBytes, record.PeakRetainedBytes,
            record.DiskBytes,
            (record.SavedTargets + record.SkippedTargets - job.StartedCompleted) / elapsed,
            record.ThrottleReason, record.LastError, record.UpdatedUtc);
    }

    private Job Get(string id) => _jobs.GetValueOrDefault(id)
        ?? throw new KeyNotFoundException($"Unknown pregeneration job '{id}'.");

    private static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 64 || id.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new ArgumentException(
                "Job IDs may contain only ASCII letters, digits, '.', '-', and '_' (maximum 64 characters).",
                nameof(id));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activeCancellation?.Cancel();
        try
        {
            _activeWork?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException error) when (error.InnerExceptions.All(static inner =>
                   inner is OperationCanceledException))
        {
        }
        ClearActive();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed class Job(PersistedJob record)
    {
        private IEnumerator<ChunkPos>? _traversal;
        private long _traversalOrdinal = -1;
        public PersistedJob Record = record;
        public long StartedAt { get; } = Stopwatch.GetTimestamp();
        public long StartedCompleted { get; } = record.SavedTargets + record.SkippedTargets;

        public ChunkPos GetTarget()
        {
            if (_traversal is null || _traversalOrdinal > Record.NextTarget)
            {
                _traversal?.Dispose();
                _traversal = CircularChunkTraversal.Enumerate(
                    Record.Definition.CenterChunkX,
                    Record.Definition.CenterChunkZ,
                    Record.Definition.RadiusChunks).GetEnumerator();
                _traversalOrdinal = -1;
            }
            while (_traversalOrdinal < Record.NextTarget)
            {
                if (!_traversal.MoveNext())
                    throw new InvalidDataException(
                        $"Pregeneration job '{Record.Definition.Id}' cursor exceeds its circular target.");
                _traversalOrdinal++;
            }
            return _traversal.Current;
        }
    }

    private sealed record PersistedJob(
        int FormatVersion,
        FixedAreaPregenerationDefinition Definition,
        FixedAreaPregenerationStatus Status,
        long NextTarget,
        long PreparedTargets,
        long DecoratedTargets,
        long SavedTargets,
        long SkippedTargets,
        long WrittenChunks,
        long RetainedBytes,
        long PeakRetainedBytes,
        long DiskBytes,
        string ThrottleReason,
        string? LastError,
        DateTimeOffset UpdatedUtc);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int LinuxOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int LinuxFsync(int descriptor);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int LinuxClose(int descriptor);
}

/// <summary>Shared durable one-target transaction used by fixed and moving preparation owners.</summary>
internal static class InactiveGenerationTargetExecutor
{
    public static async Task<FixedAreaPregenerationWorkResult> Execute(
        ServerWorld world,
        ChunkMap chunkMap,
        IChunkStorage storage,
        DirectoryInfo stateDirectory,
        string checkpointId,
        string owner,
        ChunkPos position,
        int radialDistance,
        CancellationToken cancellationToken)
    {
        if (chunkMap.HasGameplayOwnershipInDecorationHalo(position))
            return new FixedAreaPregenerationWorkResult(false, true, 0, 0, 0);

        using var request = chunkMap.RequestBackgroundDecoration(
            [position], owner, radialDistance);
        using var registration = cancellationToken.Register(request.Dispose);
        var batch = await request.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var skipped = batch.SkippedTargets.Contains(position);
        var writableStored = batch.WritableStoredTargets.ToHashSet();

        // A player save can race the isolated workspace. Discard and rediscover rather than let
        // stale background output overwrite the newer authoritative chunk.
        if (batch.Chunks.Any(chunk =>
                chunkMap.HasGameplayOwnership(chunk.X, chunk.Z) ||
                (!writableStored.Contains(new ChunkPos(chunk.X, chunk.Z)) &&
                 storage.ContainsChunk(chunk.X, chunk.Z))))
            return new FixedAreaPregenerationWorkResult(false, true, 0, 0, 0);

        var retainedBytes = batch.RetainedBytes;
        var checkpoint = new InactiveGenerationCheckpointStore(stateDirectory, checkpointId);
        var sequence = checked(checkpoint.Recover().Checkpoint.LastCommittedBatch + 1);
        var commit = checkpoint.CommitBatch(sequence, batch, world, storage, cancellationToken);
        foreach (var snapshot in batch.Chunks)
            world.SubmitOfflineTerrainLod(snapshot);
        return new FixedAreaPregenerationWorkResult(
            skipped,
            false,
            commit.TerrainCommit.Chunks.Count,
            retainedBytes,
            commit.TerrainCommit.SizeDeltaBytes);
    }
}

internal static class CircularChunkTraversal
{
    public static long Count(int radius)
    {
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        var radiusSquared = (long)radius * radius;
        long count = 0;
        for (var x = -radius; x <= radius; x++)
        {
            var remaining = radiusSquared - (long)x * x;
            var maximumZ = (long)Math.Sqrt(remaining);
            // Guard the floating conversion even though the supported radius is small enough for
            // exact integer squares in a double. This keeps Count correct if that limit grows.
            while (checked((maximumZ + 1) * (maximumZ + 1)) <= remaining) maximumZ++;
            while (maximumZ * maximumZ > remaining) maximumZ--;
            count = checked(count + maximumZ * 2 + 1);
        }
        return count;
    }

    public static ChunkPos At(int centerX, int centerZ, int radius, long ordinal)
    {
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        long current = 0;
        foreach (var position in Enumerate(centerX, centerZ, radius))
        {
            if (current++ == ordinal) return position;
        }
        throw new ArgumentOutOfRangeException(nameof(ordinal));
    }

    public static IEnumerable<ChunkPos> Enumerate(int centerX, int centerZ, int radius)
    {
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        yield return new ChunkPos(centerX, centerZ);
        var radiusSquared = (long)radius * radius;
        for (var ring = 1; ring <= radius; ring++)
        {
            for (var x = -ring; x <= ring; x++)
            {
                if ((long)x * x + (long)(-ring) * -ring <= radiusSquared)
                    yield return new ChunkPos(checked(centerX + x), checked(centerZ - ring));
            }
            for (var z = -ring + 1; z <= ring; z++)
            {
                if ((long)ring * ring + (long)z * z <= radiusSquared)
                    yield return new ChunkPos(checked(centerX + ring), checked(centerZ + z));
            }
            for (var x = ring - 1; x >= -ring; x--)
            {
                if ((long)x * x + (long)ring * ring <= radiusSquared)
                    yield return new ChunkPos(checked(centerX + x), checked(centerZ + ring));
            }
            for (var z = ring - 1; z > -ring; z--)
            {
                if ((long)(-ring) * -ring + (long)z * z <= radiusSquared)
                    yield return new ChunkPos(checked(centerX - ring), checked(centerZ + z));
            }
        }
    }

    public static int RingDistance(int centerX, int centerZ, ChunkPos position) =>
        Math.Max(Math.Abs(position.X - centerX), Math.Abs(position.Z - centerZ));
}
