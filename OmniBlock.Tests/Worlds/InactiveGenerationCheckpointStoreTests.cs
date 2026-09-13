using OmniBlock.Server.Worlds;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Tests.Worlds;

public sealed class InactiveGenerationCheckpointStoreTests
{
    [Fact]
    public void Checkpoint_advances_only_after_terrain_is_durably_flushed_and_recovers_after_restart()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var batch = CreateBatch(world, new ChunkPos(2, -3));
            var storage = new RecordingStorage();
            var observedCheckpointAfterFlush = false;
            var store = new InactiveGenerationCheckpointStore(root, "fixture", stage =>
            {
                if (stage != InactiveGenerationCheckpointStage.CheckpointDurable) return;
                observedCheckpointAfterFlush = storage.FlushedToDisk;
            });

            var result = store.CommitBatch(0, batch, world, storage);

            Assert.True(observedCheckpointAfterFlush);
            Assert.Equal(0, result.Checkpoint.LastCommittedBatch);
            Assert.Equal(1, result.Checkpoint.CommittedTargets);
            Assert.Equal(1, result.Checkpoint.WrittenChunks);
            Assert.Equal(3, result.Checkpoint.SizeDeltaBytes);

            var recovered = new InactiveGenerationCheckpointStore(root, "fixture").Recover();
            Assert.Equal(InactiveGenerationRecoveryStatus.Clean, recovered.Status);
            Assert.Equal(result.Checkpoint, recovered.Checkpoint);
            Assert.Null(recovered.PendingBatch);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Failed_terrain_commit_keeps_an_interrupted_marker_and_can_retry_the_same_batch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var batch = CreateBatch(world, new ChunkPos(4, 5));
            var store = new InactiveGenerationCheckpointStore(root, "retry");

            Assert.Throws<InactiveGenerationCommitException>(() =>
                store.CommitBatch(0, batch, world, new RecordingStorage(failFlush: true)));

            var interrupted = new InactiveGenerationCheckpointStore(root, "retry").Recover();
            Assert.Equal(InactiveGenerationRecoveryStatus.InterruptedBatch, interrupted.Status);
            Assert.Equal(-1, interrupted.Checkpoint.LastCommittedBatch);
            Assert.Equal(0, interrupted.PendingBatch!.Sequence);
            Assert.Equal(
                [new InactiveGenerationCheckpointTarget(4, 5)],
                interrupted.PendingBatch.Targets);

            var retried = new InactiveGenerationCheckpointStore(root, "retry")
                .CommitBatch(0, batch, world, new RecordingStorage());
            Assert.Equal(0, retried.Checkpoint.LastCommittedBatch);
            Assert.Equal(
                InactiveGenerationRecoveryStatus.Clean,
                new InactiveGenerationCheckpointStore(root, "retry").Recover().Status);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Restart_distinguishes_committed_terrain_from_interrupted_marker_cleanup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var firstBatch = CreateBatch(world, new ChunkPos(0, 0));
            var interruptedCleanup = new InactiveGenerationCheckpointStore(root, "cleanup", stage =>
            {
                if (stage == InactiveGenerationCheckpointStage.CheckpointDurable)
                    throw new SimulatedProcessInterruption();
            });

            Assert.Throws<SimulatedProcessInterruption>(() =>
                interruptedCleanup.CommitBatch(0, firstBatch, world, new RecordingStorage()));

            var recovered = new InactiveGenerationCheckpointStore(root, "cleanup").Recover();
            Assert.Equal(InactiveGenerationRecoveryStatus.CommittedBatchCleanupPending, recovered.Status);
            Assert.Equal(0, recovered.Checkpoint.LastCommittedBatch);
            Assert.Equal(0, recovered.PendingBatch!.Sequence);

            var secondBatch = CreateBatch(world, new ChunkPos(1, 0));
            var continued = new InactiveGenerationCheckpointStore(root, "cleanup")
                .CommitBatch(1, secondBatch, world, new RecordingStorage());
            Assert.Equal(1, continued.Checkpoint.LastCommittedBatch);
            Assert.Equal(2, continued.Checkpoint.CommittedTargets);
            Assert.Equal(InactiveGenerationRecoveryStatus.Clean,
                new InactiveGenerationCheckpointStore(root, "cleanup").Recover().Status);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Interrupted_batch_must_retry_the_same_targets_before_advancing()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var world = new FakeWorldContext();
            var original = CreateBatch(world, new ChunkPos(7, 8));
            var store = new InactiveGenerationCheckpointStore(root, "identity");
            Assert.Throws<InactiveGenerationCommitException>(() =>
                store.CommitBatch(0, original, world, new RecordingStorage(failFlush: true)));

            var changed = CreateBatch(world, new ChunkPos(8, 8));
            var error = Assert.Throws<InvalidOperationException>(() =>
                new InactiveGenerationCheckpointStore(root, "identity")
                    .CommitBatch(0, changed, world, new RecordingStorage()));

            Assert.Contains("same decoration targets", error.Message);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    [Fact]
    public void Region_world_storage_places_checkpoints_under_its_world_data_directory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var saveDirectory = new DirectoryInfo(Path.Combine(root.FullName, "fixture-world"));
            var storage = new RegionWorldStorage(saveDirectory, createPlayersDir: false);
            var store = new InactiveGenerationCheckpointStore(storage, "path-fixture");

            var recovered = store.Recover();

            Assert.Equal(InactiveGenerationCheckpoint.Empty("path-fixture"), recovered.Checkpoint);
            Assert.True(Directory.Exists(Path.Combine(
                saveDirectory.FullName,
                "data",
                "worldgen")));
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
        }
    }

    private static InactiveGenerationBatch CreateBatch(FakeWorldContext world, ChunkPos target)
    {
        var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], target.X, target.Z)
        {
            TerrainPopulated = true
        };
        return new InactiveGenerationBatch([InactiveChunkSnapshot.Capture(chunk, world)])
        {
            DecoratedTargets = [target]
        };
    }

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var directory = new DirectoryInfo(
            Path.Combine(Path.GetTempPath(), $"omniblock-worldgen-checkpoint-{Guid.NewGuid():N}"));
        directory.Create();
        return directory;
    }

    private sealed class RecordingStorage(bool failFlush = false) : IChunkStorage
    {
        public bool FlushedToDisk { get; private set; }
        public Chunk? LoadChunk(IWorldContext world, int chunkX, int chunkZ) => null;
        public ChunkSaveResult SaveChunk(IWorldContext world, Chunk chunk, Action? onSave, long sequence)
        {
            onSave?.Invoke();
            return new ChunkSaveResult(3);
        }
        public void SaveEntities(IWorldContext world, Chunk chunk) { }
        public void Tick() { }
        public void Flush() { }
        public void FlushToDisk()
        {
            FlushedToDisk = true;
            if (failFlush) throw new IOException("Simulated durable flush failure.");
        }
    }

    private sealed class SimulatedProcessInterruption : Exception;
}
