using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Server.Worlds;

internal enum InactiveGenerationRecoveryStatus
{
    Clean,
    InterruptedBatch,
    CommittedBatchCleanupPending
}

internal sealed record InactiveGenerationCheckpoint(
    int FormatVersion,
    string JobId,
    long LastCommittedBatch,
    long CommittedTargets,
    long WrittenChunks,
    long SizeDeltaBytes)
{
    public const int CurrentFormatVersion = 1;

    public static InactiveGenerationCheckpoint Empty(string jobId) =>
        new(CurrentFormatVersion, jobId, -1, 0, 0, 0);
}

internal readonly record struct InactiveGenerationCheckpointTarget(int X, int Z)
{
    public ChunkPos ToChunkPos() => new(X, Z);
}

internal sealed record InactiveGenerationPendingBatch(
    int FormatVersion,
    string JobId,
    long Sequence,
    IReadOnlyList<InactiveGenerationCheckpointTarget> Targets);

internal sealed record InactiveGenerationRecovery(
    InactiveGenerationCheckpoint Checkpoint,
    InactiveGenerationPendingBatch? PendingBatch,
    InactiveGenerationRecoveryStatus Status,
    bool InterruptedMetadataWrite);

internal sealed record InactiveGenerationCheckpointCommit(
    InactiveGenerationCheckpoint Checkpoint,
    InactiveGenerationCommit TerrainCommit);

internal enum InactiveGenerationCheckpointStage
{
    PendingDurable,
    TerrainDurable,
    CheckpointDurable,
    CleanupDurable
}

/// <summary>
///     Persists the minimal monotonic cursor needed by the future deterministic job traversal.
///     A write-ahead marker makes process interruption distinguishable from completed terrain.
/// </summary>
internal sealed class InactiveGenerationCheckpointStore
{
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenDirectory = 0x10000;

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly DirectoryInfo _directory;
    private readonly string _jobId;
    private readonly string _checkpointPath;
    private readonly string _checkpointTempPath;
    private readonly string _pendingPath;
    private readonly string _pendingTempPath;
    private readonly Action<InactiveGenerationCheckpointStage>? _stageObserver;
    private readonly object _gate = new();

    public InactiveGenerationCheckpointStore(
        DirectoryInfo directory,
        string jobId,
        Action<InactiveGenerationCheckpointStage>? stageObserver = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        _directory = directory;
        _jobId = jobId;
        _stageObserver = stageObserver;
        var safeName = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));
        _checkpointPath = Path.Combine(directory.FullName, $"{safeName}.checkpoint.json");
        _checkpointTempPath = $"{_checkpointPath}.tmp";
        _pendingPath = Path.Combine(directory.FullName, $"{safeName}.pending.json");
        _pendingTempPath = $"{_pendingPath}.tmp";
    }

    public InactiveGenerationCheckpointStore(
        IWorldStorage storage,
        string jobId,
        Action<InactiveGenerationCheckpointStage>? stageObserver = null)
        : this(
            storage?.GetWorldGenerationStateDirectory()
            ?? throw new NotSupportedException(
                "This world storage does not support persistent generation checkpoints."),
            jobId,
            stageObserver)
    {
    }

    public InactiveGenerationRecovery Recover()
    {
        lock (_gate)
        {
            var checkpoint = File.Exists(_checkpointPath)
                ? Read<InactiveGenerationCheckpoint>(_checkpointPath)
                : InactiveGenerationCheckpoint.Empty(_jobId);
            ValidateCheckpoint(checkpoint);

            var pending = File.Exists(_pendingPath)
                ? Read<InactiveGenerationPendingBatch>(_pendingPath)
                : null;
            if (pending != null) ValidatePending(checkpoint, pending);

            var status = pending switch
            {
                null => InactiveGenerationRecoveryStatus.Clean,
                _ when pending.Sequence <= checkpoint.LastCommittedBatch =>
                    InactiveGenerationRecoveryStatus.CommittedBatchCleanupPending,
                _ => InactiveGenerationRecoveryStatus.InterruptedBatch
            };
            return new InactiveGenerationRecovery(
                checkpoint,
                pending,
                status,
                File.Exists(_checkpointTempPath) || File.Exists(_pendingTempPath));
        }
    }

    public InactiveGenerationCheckpointCommit CommitBatch(
        long sequence,
        InactiveGenerationBatch batch,
        IWorldContext world,
        IChunkStorage storage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(storage);

        lock (_gate)
        {
            var recovery = Recover();
            CleanupCommittedMarker(recovery);
            recovery = Recover();
            var checkpoint = recovery.Checkpoint;
            var expectedSequence = checked(checkpoint.LastCommittedBatch + 1);
            if (sequence != expectedSequence)
                throw new InvalidOperationException(
                    $"Generation job '{_jobId}' expected batch {expectedSequence}, got {sequence}.");

            var targets = batch.DecoratedTargets
                .Distinct()
                .OrderBy(static target => target.X)
                .ThenBy(static target => target.Z)
                .Select(static target => new InactiveGenerationCheckpointTarget(target.X, target.Z))
                .ToArray();
            if (targets.Length == 0)
                throw new InvalidOperationException(
                    $"Generation job '{_jobId}' cannot commit a batch without decoration targets.");
            if (recovery.PendingBatch is { } interrupted)
            {
                if (interrupted.Sequence != sequence || !interrupted.Targets.SequenceEqual(targets))
                    throw new InvalidOperationException(
                        $"Generation job '{_jobId}' must retry interrupted batch {interrupted.Sequence} " +
                        "with the same decoration targets before advancing.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            WriteAtomic(_pendingPath, _pendingTempPath,
                new InactiveGenerationPendingBatch(
                    InactiveGenerationCheckpoint.CurrentFormatVersion,
                    _jobId,
                    sequence,
                    targets));
            _stageObserver?.Invoke(InactiveGenerationCheckpointStage.PendingDurable);

            var terrainCommit = batch.SaveDurably(
                world,
                storage,
                checkpoint.WrittenChunks,
                cancellationToken);
            _stageObserver?.Invoke(InactiveGenerationCheckpointStage.TerrainDurable);

            var next = checkpoint with
            {
                LastCommittedBatch = sequence,
                CommittedTargets = checked(checkpoint.CommittedTargets + targets.Length),
                WrittenChunks = checked(checkpoint.WrittenChunks + terrainCommit.Chunks.Count),
                SizeDeltaBytes = checked(checkpoint.SizeDeltaBytes + terrainCommit.SizeDeltaBytes)
            };
            WriteAtomic(_checkpointPath, _checkpointTempPath, next);
            _stageObserver?.Invoke(InactiveGenerationCheckpointStage.CheckpointDurable);

            DeleteMetadata(_pendingPath);
            DeleteMetadata(_pendingTempPath);
            DeleteMetadata(_checkpointTempPath);
            FlushDirectory();
            _stageObserver?.Invoke(InactiveGenerationCheckpointStage.CleanupDurable);
            return new InactiveGenerationCheckpointCommit(next, terrainCommit);
        }
    }

    private void CleanupCommittedMarker(InactiveGenerationRecovery recovery)
    {
        if (recovery.Status != InactiveGenerationRecoveryStatus.CommittedBatchCleanupPending) return;
        DeleteMetadata(_pendingPath);
        DeleteMetadata(_pendingTempPath);
        DeleteMetadata(_checkpointTempPath);
        FlushDirectory();
    }

    private void ValidateCheckpoint(InactiveGenerationCheckpoint checkpoint)
    {
        if (checkpoint.FormatVersion != InactiveGenerationCheckpoint.CurrentFormatVersion)
            throw new InvalidDataException(
                $"Generation checkpoint for '{_jobId}' uses unsupported format {checkpoint.FormatVersion}.");
        if (!string.Equals(checkpoint.JobId, _jobId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Generation checkpoint belongs to '{checkpoint.JobId}', expected '{_jobId}'.");
        if (checkpoint.LastCommittedBatch < -1 || checkpoint.CommittedTargets < 0 ||
            checkpoint.WrittenChunks < 0 || checkpoint.SizeDeltaBytes < 0)
            throw new InvalidDataException($"Generation checkpoint for '{_jobId}' has invalid counters.");
        if (checkpoint.LastCommittedBatch == -1 &&
            (checkpoint.CommittedTargets != 0 || checkpoint.WrittenChunks != 0 ||
             checkpoint.SizeDeltaBytes != 0))
            throw new InvalidDataException(
                $"Empty generation checkpoint for '{_jobId}' has non-zero counters.");
    }

    private void ValidatePending(
        InactiveGenerationCheckpoint checkpoint,
        InactiveGenerationPendingBatch pending)
    {
        if (pending.FormatVersion != InactiveGenerationCheckpoint.CurrentFormatVersion)
            throw new InvalidDataException(
                $"Pending generation batch for '{_jobId}' uses unsupported format {pending.FormatVersion}.");
        if (!string.Equals(pending.JobId, _jobId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Pending generation batch belongs to '{pending.JobId}', expected '{_jobId}'.");
        if (pending.Sequence < 0 || pending.Sequence > checkpoint.LastCommittedBatch + 1)
            throw new InvalidDataException(
                $"Pending generation batch {pending.Sequence} is inconsistent with checkpoint " +
                $"{checkpoint.LastCommittedBatch} for '{_jobId}'.");
        if (pending.Sequence != checkpoint.LastCommittedBatch &&
            pending.Sequence != checkpoint.LastCommittedBatch + 1)
            throw new InvalidDataException(
                $"Pending generation batch {pending.Sequence} is stale relative to checkpoint " +
                $"{checkpoint.LastCommittedBatch} for '{_jobId}'.");
        var canonicalTargets = pending.Targets
            .Distinct()
            .OrderBy(static target => target.X)
            .ThenBy(static target => target.Z);
        if (pending.Targets.Count == 0 || !pending.Targets.SequenceEqual(canonicalTargets))
            throw new InvalidDataException(
                $"Pending generation batch {pending.Sequence} for '{_jobId}' has non-canonical targets.");
    }

    private static T Read<T>(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return JsonSerializer.Deserialize<T>(stream, s_jsonOptions)
                   ?? throw new InvalidDataException($"Generation metadata '{path}' is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Generation metadata '{path}' is invalid JSON.", error);
        }
    }

    private void WriteAtomic<T>(string path, string temporaryPath, T value)
    {
        _directory.Create();
        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, value, s_jsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path, overwrite: true);
        FlushDirectory();
    }

    private static void DeleteMetadata(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private void FlushDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;
        var descriptor = LinuxOpen(
            _directory.FullName,
            LinuxOpenReadOnly | LinuxOpenDirectory);
        if (descriptor < 0)
            throw new IOException(
                $"Could not open generation metadata directory '{_directory.FullName}' for durable flush.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (LinuxFsync(descriptor) != 0)
                throw new IOException(
                    $"Could not durably flush generation metadata directory '{_directory.FullName}'.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally
        {
            LinuxClose(descriptor);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int LinuxOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int LinuxFsync(int descriptor);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int LinuxClose(int descriptor);
}
