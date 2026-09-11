using System.Diagnostics;
using OmniBlock.Client.Rendering.Chunks;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class MeshLifecycleDiagnosticsTests
{
    private static MeshLifecycleRequest Queue(MeshLifecycleDiagnostics diagnostics) =>
        diagnostics.Queue(1, new Vector3D<int>(16, 32, -48), 3,
            MeshWorkPriority.Foreground, SectionDirtyReason.InitialTerrain);

    [Fact]
    public void Upload_is_not_presentation_and_each_stage_is_recorded_once()
    {
        var diagnostics = new MeshLifecycleDiagnostics();
        var request = Queue(diagnostics);
        Assert.Equal(1, diagnostics.Snapshot().Queued);
        diagnostics.Move(request, MeshLifecycleStage.Snapshotting);
        diagnostics.Move(request, MeshLifecycleStage.WorkerQueued);
        diagnostics.Move(request, MeshLifecycleStage.Building, MeshWorkPriority.Critical);
        diagnostics.Move(request, MeshLifecycleStage.AwaitingUpload);
        Assert.Equal(1, diagnostics.Snapshot().AwaitingUpload);
        Assert.Equal(0, diagnostics.Snapshot().Building);
        diagnostics.Move(request, MeshLifecycleStage.Uploaded);
        Assert.Equal(1, diagnostics.Snapshot().AwaitingDraw);
        Assert.Equal(0, diagnostics.Snapshot().DrawRecorded);
        diagnostics.Move(request, MeshLifecycleStage.DrawRecorded);
        diagnostics.Move(request, MeshLifecycleStage.DrawRecorded);
        diagnostics.Cancel(request, MeshCancellationReason.OutsideRetention);

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(0, snapshot.AwaitingDraw);
        Assert.Equal(1, snapshot.DrawRecorded);
        Assert.Equal(0, snapshot.Cancelled);
        var events = diagnostics.ReadEvents();
        Assert.Equal(new[] { MeshLifecycleStage.Queued, MeshLifecycleStage.Snapshotting,
            MeshLifecycleStage.WorkerQueued, MeshLifecycleStage.Building, MeshLifecycleStage.AwaitingUpload,
            MeshLifecycleStage.Uploaded, MeshLifecycleStage.DrawRecorded }, events.Select(e => e.Stage));
        Assert.All(events, e => Assert.Equal(request.Id, e.RequestId));
        Assert.Equal(MeshWorkPriority.Critical, events[^1].Priority);
        Assert.True(events.Zip(events.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));
    }

    [Theory]
    [InlineData((int)MeshCancellationReason.Superseded)]
    [InlineData((int)MeshCancellationReason.OutsideRetention)]
    [InlineData((int)MeshCancellationReason.RendererDisposed)]
    [InlineData((int)MeshCancellationReason.Orphaned)]
    [InlineData((int)MeshCancellationReason.DuplicateRequest)]
    [InlineData((int)MeshCancellationReason.BuildFailed)]
    [InlineData((int)MeshCancellationReason.SnapshotFailed)]
    [InlineData((int)MeshCancellationReason.SectionReplaced)]
    public void Cancellation_is_terminal_and_records_a_specific_reason(int reasonValue)
    {
        var reason = (MeshCancellationReason)reasonValue;
        var diagnostics = new MeshLifecycleDiagnostics();
        var request = Queue(diagnostics);
        diagnostics.Move(request, MeshLifecycleStage.Building);
        diagnostics.Cancel(request, reason);
        // The real worker can finish after cancellation: diagnostics must not resurrect its work.
        diagnostics.Move(request, MeshLifecycleStage.AwaitingUpload);
        diagnostics.Cancel(request, reason);
        var snapshot = diagnostics.Snapshot();
        Assert.Equal(1, snapshot.Cancelled);
        Assert.Equal(reason == MeshCancellationReason.Superseded ? 1 : 0, snapshot.Superseded);
        Assert.Equal(reason is MeshCancellationReason.BuildFailed or MeshCancellationReason.SnapshotFailed ? 1 : 0,
            snapshot.BuildFailures);
        Assert.Equal(0, snapshot.Building);
        Assert.Equal(0, snapshot.AwaitingUpload);
        Assert.Equal(reason, diagnostics.ReadEvents()[^1].Reason);
    }

    [Fact]
    public void Empty_sections_finish_without_a_draw_or_a_false_cancellation()
    {
        var diagnostics = new MeshLifecycleDiagnostics();
        using var section = new SectionRenderState(default, diagnostics);
        var request = section.BeginTrace(1);
        section.RecordUploaded(5, request, empty: true);
        section.Dispose();
        Assert.Equal(1, diagnostics.Snapshot().EmptyReady);
        Assert.Equal(0, diagnostics.Snapshot().AwaitingDraw);
        Assert.Equal(0, diagnostics.Snapshot().DrawRecorded);
        Assert.Equal(0, diagnostics.Snapshot().Cancelled);
    }

    [Fact]
    public void Replacement_cancels_an_uploaded_mesh_that_was_never_drawn()
    {
        var diagnostics = new MeshLifecycleDiagnostics();
        using var section = new SectionRenderState(default, diagnostics);
        section.RecordUploaded(1, section.BeginTrace(1));
        section.RecordUploaded(2, section.BeginTrace(2));
        Assert.Equal(1, diagnostics.Snapshot().Superseded);
        Assert.Equal(1, diagnostics.Snapshot().AwaitingDraw);
        section.Dispose(MeshCancellationReason.RendererDisposed);
        Assert.Equal(0, diagnostics.Snapshot().AwaitingDraw);
        Assert.Equal(2, diagnostics.Snapshot().Cancelled);
    }

    [Fact]
    public void Reused_coordinates_and_pooled_epochs_have_distinct_lifetime_and_request_ids()
    {
        var diagnostics = new MeshLifecycleDiagnostics();
        var old = new SectionRenderState(default, diagnostics);
        var oldTrace = old.BeginTrace(1)!;
        old.Dispose();
        using var current = new SectionRenderState(default, diagnostics);
        var currentTrace = current.BeginTrace(1)!;
        current.Version.MarkDirty();
        current.Version.SnapshotIfNeeded();
        var version = current.Version.State;
        old.AbandonRequest();
        old.Dispose();
        Assert.Equal(version, current.Version.State);
        Assert.NotEqual(oldTrace.SectionId, currentTrace.SectionId);
        Assert.NotEqual(oldTrace.Id, currentTrace.Id);
        Assert.False(current.OwnsResult(oldTrace.SectionId));
        Assert.False(old.OwnsResult(oldTrace.SectionId));
        Assert.True(current.OwnsResult(currentTrace.SectionId));
        Assert.Equal(1, diagnostics.Snapshot().Cancelled);
    }

    [Fact]
    public void History_rollover_is_bounded_but_does_not_reset_counters()
    {
        var diagnostics = new MeshLifecycleDiagnostics(4);
        for (var i = 0; i < 10; i++) diagnostics.Cancel(Queue(diagnostics), MeshCancellationReason.Superseded);
        Assert.Equal(10, diagnostics.Snapshot().Cancelled);
        Assert.Equal(20, diagnostics.Snapshot().Events);
        Assert.Equal(16, diagnostics.Snapshot().OverwrittenEvents);
        Assert.Equal(new long[] { 17, 18, 19, 20 }, diagnostics.ReadEvents().Select(e => e.Sequence));
        Assert.Contains("sectionId\trequestId", diagnostics.CreateDump());
    }

    [Fact]
    public void Concurrent_workers_produce_unique_ordered_events_and_balanced_gauges()
    {
        var diagnostics = new MeshLifecycleDiagnostics(64);
        Parallel.For(0, 1000, _ =>
        {
            var request = Queue(diagnostics);
            diagnostics.Move(request, MeshLifecycleStage.Building);
            diagnostics.Move(request, MeshLifecycleStage.AwaitingUpload);
            diagnostics.Cancel(request, MeshCancellationReason.RendererDisposed);
        });
        Assert.Equal(1000, diagnostics.Snapshot().Cancelled);
        Assert.Equal(4000, diagnostics.Snapshot().Events);
        Assert.Equal(0, diagnostics.Snapshot().Queued);
        Assert.Equal(0, diagnostics.Snapshot().Building);
        Assert.Equal(0, diagnostics.Snapshot().AwaitingUpload);
        Assert.Equal(64, diagnostics.ReadEvents().Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void Critical_deadline_is_measured_by_monotonic_time_not_render_rate()
    {
        long now = 0;
        var diagnostics = new MeshLifecycleDiagnostics(clock: () => now);
        var deadlineTicks = (long)(ChunkRenderer.CriticalMeshDeadlineMs * Stopwatch.Frequency / 1000.0);
        var met = diagnostics.Queue(1, default, 1, MeshWorkPriority.Critical,
            SectionDirtyReason.BlockChange, queuedFrame: 10, deadlineFrame: 12);
        now += deadlineTicks;
        diagnostics.Move(met, MeshLifecycleStage.Uploaded);
        diagnostics.Move(met, MeshLifecycleStage.DrawRecorded);
        Assert.Equal(1, diagnostics.Snapshot().CriticalCompleted);
        Assert.Equal(0, diagnostics.Snapshot().CriticalDeadlineMisses);

        var late = diagnostics.Queue(1, default, 2, MeshWorkPriority.Critical,
            SectionDirtyReason.BlockChange, queuedFrame: 20, deadlineFrame: 22);
        now += deadlineTicks + 1;
        Assert.Equal(1, diagnostics.Snapshot().CriticalOverdue);
        diagnostics.Move(late, MeshLifecycleStage.EmptyReady);
        Assert.Equal(2, diagnostics.Snapshot().CriticalCompleted);
        Assert.Equal(1, diagnostics.Snapshot().CriticalDeadlineMisses);
        Assert.Equal(0, diagnostics.Snapshot().CriticalOverdue);
        Assert.Equal(1, diagnostics.CriticalDeadlineMissesAt(default));
        Assert.Equal(0, diagnostics.CriticalDeadlineMissesAt(new Vector3D<int>(16, 0, 0)));
    }

    [Fact]
    public void Promotion_assigns_a_deadline_and_cooperative_observation_is_distinct_from_request_cancellation()
    {
        var diagnostics = new MeshLifecycleDiagnostics();
        var request = diagnostics.Queue(1, default, 1, MeshWorkPriority.Background,
            SectionDirtyReason.InitialTerrain, queuedFrame: 5);
        diagnostics.Promote(request, MeshWorkPriority.Critical, deadlineFrame: 7);
        diagnostics.Cancel(request, MeshCancellationReason.Superseded);
        diagnostics.ObserveCancellation(request, buildStarted: true, MeshCancellationReason.Superseded);

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(1, snapshot.Cancelled);
        Assert.Equal(1, snapshot.CooperativeCancellations);
        Assert.Equal(0, snapshot.CancelledBeforeBuild);
        Assert.Equal(1, snapshot.CancelledDuringBuild);
        Assert.Equal(0, snapshot.CriticalCompleted);
        var observed = diagnostics.ReadEvents()[^1];
        Assert.Equal(MeshLifecycleStage.CancellationObserved, observed.Stage);
        Assert.Equal(MeshWorkPriority.Critical, observed.Priority);
        Assert.Equal(7, observed.DeadlineFrame);
    }
}
