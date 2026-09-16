using Hexa.NET.ImGui;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Diagnostics;
using OmniBlock.Profiling;
using OmniBlock.Server.Worlds;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class ProfilerWindow(DebugWindowContext ctx) : DebugWindow
{
    private string? _captureError;
    private string? _lastCaptureDirectory;

    public override string Title => "Profiler";
    public override DebugDock DefaultDock => DebugDock.Right;

    protected override void OnDraw()
    {
        if (ImGui.CollapsingHeader("Frame timings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawWholeFrameSummary();
            DrawGpuFrameSummary();
            ProfilerRenderer.DrawContents();
            if (ImGui.Button("Capture profile")) CaptureProfile(ctx.ChunkRenderer);
            if (_lastCaptureDirectory != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Copy capture path"))
                    Display.SetClipboardString(_lastCaptureDirectory);
                ImGuiTextSafe.TextDisabled(_lastCaptureDirectory);
            }
            else if (_captureError != null)
            {
                ImGuiTextSafe.TextDisabled(_captureError);
            }
        }

        if (ImGui.CollapsingHeader("World generation"))
            DrawWorldGeneration(ctx.WorldGeneration);

        if (ctx.ChunkRenderer is not { } chunkRenderer)
        {
            ImGuiTextSafe.TextDisabled("No chunk renderer is active.");
            return;
        }

        if (ImGui.CollapsingHeader("Chunk visibility and submission", ImGuiTreeNodeFlags.DefaultOpen))
            DrawChunkPresentation(chunkRenderer);

        if (ImGui.CollapsingHeader("Chunk mesh construction", ImGuiTreeNodeFlags.DefaultOpen))
            DrawChunkMeshConstruction(chunkRenderer);

        if (ImGui.CollapsingHeader("Chunk streaming lifecycle", ImGuiTreeNodeFlags.DefaultOpen))
            DrawChunkLifecycle(chunkRenderer);

        if (ImGui.CollapsingHeader("Distant terrain LOD", ImGuiTreeNodeFlags.DefaultOpen))
            DrawTerrainLod(ctx.TerrainLod);

        if (ImGui.CollapsingHeader("Non-terrain presentation", ImGuiTreeNodeFlags.DefaultOpen))
            DrawNonTerrainPresentation();
    }

    private static void DrawWorldGeneration(WorldGenerationSnapshot? profile)
    {
        if (profile is null)
        {
            ImGuiTextSafe.TextDisabled("World generation profiling is available for the integrated server.");
            return;
        }

        ImGuiTextSafe.Text(
            $"Work: queued {profile.Queued}  running {profile.InFlight}  ready {profile.Ready}  peak {profile.QueuePeak}");
        ImGuiTextSafe.Text(
            $"Resident: {profile.RetainedChunks:N0} chunks  payload >= {FormatBytes(profile.RetainedPayloadBytes)}  failures {profile.Failures:N0}");

        foreach (var stage in Enum.GetValues<WorldGenerationStage>())
        {
            var timing = profile.Stages[stage];
            if (timing.Count == 0) continue;
            ImGuiTextSafe.Text(
                $"{stage}: {timing.Count:N0}  avg {timing.AverageMs:F3} ms  p50 {timing.P50Ms:F3}  p95 {timing.P95Ms:F3}  p99 {timing.P99Ms:F3}  max {timing.MaxMs:F3}");
            ImGuiTextSafe.TextDisabled(
                $"  allocated avg {FormatBytes(timing.AverageAllocatedBytes)}  p95 {FormatBytes(timing.P95AllocatedBytes)}  max {FormatBytes(timing.MaxAllocatedBytes)}");
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024L => $"{bytes / (1024.0 * 1024.0):F1} MiB",
        >= 1024L => $"{bytes / 1024.0:F1} KiB",
        _ => $"{bytes} B"
    };

    private void DrawWholeFrameSummary()
    {
        var frame = Profiler.GetStats().FirstOrDefault(
            static entry => entry.Name == "[Main] FrameTime");
        if (frame.Name != null)
        {
            ImGuiTextSafe.Text(
                $"Whole frame: {frame.Last:F2} ms ({FramesPerSecond(frame.Last):F1} FPS)  " +
                $"avg {frame.Avg:F2} ms ({FramesPerSecond(frame.Avg):F1} FPS)  " +
                $"period max {frame.Max:F2} ms");
        }

        ImGuiTextSafe.TextDisabled(
            $"Limiter: {(ctx.FrameRateLimit?.ToString() ?? "unlimited")} FPS  " +
            $"VSync: {(ctx.VSync ? "on" : "off")}");

        static double FramesPerSecond(double milliseconds) =>
            milliseconds > 0 ? 1000.0 / milliseconds : 0;
    }

    private static void DrawGpuFrameSummary()
    {
        if (WebGpuDevice.Current?.GpuProfiler is not { } profiler)
        {
            ImGuiTextSafe.TextDisabled("GPU timing: no WebGPU device");
            return;
        }

        var snapshot = profiler.Latest;
        ImGuiTextSafe.TextDisabled(
            $"GPU timing: {snapshot.Status}  {snapshot.Width}x{snapshot.Height}  delayed frame {snapshot.Frame}  dropped {profiler.DroppedFrames}");
        Draw("GPU render span", snapshot.RenderSpanRawTicks, snapshot.RenderSpanMilliseconds, 1);
        Draw("  World", snapshot.World.RawTicks, snapshot.World.Milliseconds, snapshot.World.PhysicalPasses);
        Draw("  Impostor capture", snapshot.EntityImpostorCapture.RawTicks,
            snapshot.EntityImpostorCapture.Milliseconds, snapshot.EntityImpostorCapture.PhysicalPasses);
        Draw("  First-person hand", snapshot.FirstPersonHand.RawTicks,
            snapshot.FirstPersonHand.Milliseconds, snapshot.FirstPersonHand.PhysicalPasses);
        Draw("  Interface", snapshot.Interface.RawTicks,
            snapshot.Interface.Milliseconds, snapshot.Interface.PhysicalPasses);
        Draw("  Composite", snapshot.Composite.RawTicks,
            snapshot.Composite.Milliseconds, snapshot.Composite.PhysicalPasses);
        return;

        static void Draw(string name, ulong ticks, double? milliseconds, int passes)
        {
            if (milliseconds is { } value)
                ImGuiTextSafe.Text($"{name}: {value:F3} ms  {passes} pass{(passes == 1 ? "" : "es")}");
            else
                ImGuiTextSafe.Text($"{name}: {ticks:N0} raw ticks  {passes} pass{(passes == 1 ? "" : "es")}");
        }
    }

    private void CaptureProfile(ChunkRenderer? chunkRenderer)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var directory = Path.Combine(ctx.GameDataDir, "profiles", stamp);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "frame-profiler.tsv"),
                Profiler.CreateTsvSnapshot());
            if (WebGpuDevice.Current?.GpuProfiler is { } gpuProfiler)
                File.WriteAllText(
                    Path.Combine(directory, "gpu-profiler.tsv"),
                    gpuProfiler.CreateTsvSnapshot());
            if (chunkRenderer != null)
            {
                File.WriteAllText(
                    Path.Combine(directory, "chunk-presentation.tsv"),
                    chunkRenderer.CreatePresentationProfileDump());
            }

            _lastCaptureDirectory = directory;
            _captureError = null;
        }
        catch (Exception ex)
        {
            _captureError = $"Capture failed: {ex.Message}";
            _lastCaptureDirectory = null;
        }
    }

    private static void DrawChunkPresentation(ChunkRenderer chunkRenderer)
    {
        var profile = chunkRenderer.PresentationProfile;
        ImGuiTextSafe.Text(
            $"Resident:   {profile.ResidentSections} sections  {profile.ResidentSolidLayers} solid  {profile.ResidentTranslucentLayers} translucent");
        ImGuiTextSafe.Text(
            $"Presented:  {profile.PresentedSections} sections  {profile.PresentedSolidLayers} solid  {profile.PresentedTranslucentLayers} translucent");
        ImGuiTextSafe.Text(
            $"Visibility: {profile.VisibilityCandidates} candidates  {profile.FrustumTests} frustum tests  {profile.PortalVisited} portal visited");
        ImGuiTextSafe.Text(
            $"Spatial:    {profile.SpatialRegionTests} regions  {profile.SpatialColumnTests} columns  {profile.SpatialSectionTests} sections");
        ImGuiTextSafe.Text(
            $"Candidates: {profile.SpatialFrustumCandidates} frustum  {profile.SpatialCandidatesOutsideRenderDistance} beyond radius  {profile.SpatialSortComparisons:N0} sort comparisons");
        ImGuiTextSafe.Text(
            $"Fallback:   {profile.DisconnectedSeeds} disconnected seeds  {profile.SafetyRescued} safety rescued  {profile.EmptyLayersSubmitted} empty submitted");
        ImGuiTextSafe.Text(
            $"Portal:     {profile.PortalQueuePops:N0} pops  {profile.PortalEdgeAttempts:N0} edges  {profile.PortalSuccessfulReaches:N0} reached  {profile.PortalDuplicateReaches:N0} duplicate");
        ImGuiTextSafe.Text(
            $"Portal test:{profile.PortalDrawFrustumTests:N0} draw  {profile.PortalMarginFrustumTests:N0} margin  {profile.PortalMarginCacheHits:N0} cached  {profile.PortalMarginRejected:N0} rejected  {profile.PortalMissingNeighbors:N0} missing");
        ImGuiTextSafe.Text(
            $"Rescue:     {profile.IncompleteAdjacencyRescued} adjacency  {profile.NewPresentationRescued} new  {profile.PresentationRegressionRescued} regression  oldest {profile.OldestSafetyRescueFrames} frames");
        ImGuiTextSafe.Text(
            $"Submission: {profile.TerrainDrawCalls} draws  {profile.TerrainUniformEntries} uniforms");
        ImGuiTextSafe.Text(
            $"Directions: {profile.SubmittedQuads}/{profile.AvailableQuads} quads  {profile.DirectionRejectedQuads} rejected  {profile.UnassignedQuads} unassigned  {profile.DirectionDrawRanges} ranges");
        ImGuiTextSafe.Text(
            $"Batches:    {profile.TerrainSubmissionBatches} writes  {profile.TerrainPipelineBinds} pipeline binds  {profile.TerrainTextureBinds} texture binds");
        ImGuiTextSafe.Text(
            $"Streams:    {profile.TerrainStreamBinds:N0} geometry/light slice binds");
        ImGuiTextSafe.Text(
            $"Uniform arena: {profile.TerrainUniformArenaCapacity:N0} entries  {profile.TerrainUniformArenaGrowths} lifetime growths");
        var arena = chunkRenderer.TerrainGpuArenaProfile;
        ImGuiTextSafe.Text(
            $"Terrain arenas: {arena.Regions:N0} regions  {arena.GeometrySegments:N0} geometry segments  {arena.LightingSegments:N0} light segments");
        ImGuiTextSafe.Text(
            $"Arena memory: {FormatBytes(arena.AllocatedBytes)} used / {FormatBytes(arena.CapacityBytes)} reserved  {arena.ExternalFragmentation:P1} external fragmentation");
        ImGuiTextSafe.Text(
            $"Arena ranges: {arena.ActiveAllocations:N0} active  {arena.PendingRetirements:N0} retiring  {arena.SegmentGrowths:N0} segment allocations");
        DrawTiming("Find visible", profile.FindVisible);
        DrawTiming("  Spatial cull", profile.SpatialCull);
        DrawTiming("  Candidate sort", profile.CandidateSort);
        DrawTiming("  Portal traversal", profile.PortalTraversal);
        DrawTiming("Terrain submit", profile.TerrainSubmit);
    }

    private static void DrawTiming(
        string label,
        FrameTimingSnapshot timing)
    {
        ImGuiTextSafe.Text(
            $"{label}: {timing.LastMs:F3} ms  avg {timing.AverageMs:F3}  p50 {timing.P50Ms:F3}  p95 {timing.P95Ms:F3}  max {timing.MaxMs:F3}");
    }

    private static void DrawNonTerrainPresentation()
    {
        ImGuiTextSafe.Text(
            $"Entities: {MetricRegistry.Get(RenderMetrics.EntitiesRendered)} rendered  " +
            $"{MetricRegistry.Get(RenderMetrics.EntitiesHidden)} hidden  " +
            $"{MetricRegistry.Get(RenderMetrics.EntitiesTotal)} total");
        ImGuiTextSafe.Text(
            $"Block entities: {MetricRegistry.Get(RenderMetrics.BlockEntitiesRendered)} rendered  " +
            $"{MetricRegistry.Get(RenderMetrics.BlockEntitiesHidden)} hidden  " +
            $"{MetricRegistry.Get(RenderMetrics.BlockEntitiesTotal)} total");
        ImGuiTextSafe.Text(
            $"Particles: {MetricRegistry.Get(RenderMetrics.ParticlesRendered)} rendered  " +
            $"{MetricRegistry.Get(RenderMetrics.ParticlesHidden)} hidden  " +
            $"{MetricRegistry.Get(RenderMetrics.ParticlesActive)} active");
        ImGuiTextSafe.Text(
            $"Presentation quality: {MetricRegistry.Get(RenderMetrics.PresentationQuality)} " +
            "(0 fast, 1 balanced, 2 fancy)");
        ImGuiTextSafe.TextDisabled(
            "Frame timings above separate terrain, entities, and particles.");
    }

    private static void DrawChunkMeshConstruction(ChunkRenderer chunkRenderer)
    {
        var mesh = chunkRenderer.MeshProfile;
        ImGuiTextSafe.Text(
            $"Workers:   {mesh.Workers}  queued {mesh.Queued}  outstanding {mesh.Outstanding}");
        ImGuiTextSafe.Text(
            $"Results:   critical {mesh.CriticalResults}  foreground {mesh.ForegroundResults}  background {mesh.BackgroundResults}");
        ImGuiTextSafe.Text($"Built:     {mesh.Meshes:N0}");
        ImGuiTextSafe.Text(
            $"Rebuilds:  {mesh.PartialSectionBuilds:N0} partial  {mesh.FullSectionBuilds:N0} full  {mesh.Pages:N0} pages");
        ImGuiTextSafe.Text($"Cells:     {mesh.BlockCellsVisited:N0} classified/rendered");
        ImGuiTextSafe.Text($"Snapshot:  {mesh.SnapshotMs:F3} ms avg");
        ImGuiTextSafe.Text($"Queue wait:{mesh.QueueWaitMs,7:F3} ms avg");
        ImGuiTextSafe.Text($"Classify:  {mesh.ClassificationMs:F3} ms avg");
        ImGuiTextSafe.Text($"Geometry:  {mesh.GeometryMs:F3} ms avg");
        ImGuiTextSafe.Text($"Visibility:{mesh.VisibilityMs,7:F3} ms avg");
        ImGuiTextSafe.Text($"Generate:  {mesh.GenerationMs:F3} ms avg");
        ImGuiTextSafe.Text($"Upload:    {mesh.UploadMs:F3} ms avg");
        ImGuiTextSafe.Text($"Done->GPU: {mesh.FinishedToUploadMs:F3} ms avg");
        ImGuiTextSafe.Text($"Request->GPU: {mesh.RequestToUploadMs:F3} ms avg");
        var cost = chunkRenderer.MeshCostProfile;
        ImGuiTextSafe.Text(
            $"Estimate:  {cost.BuildMsPerPage:F3} ms/page  {FormatBytes(cost.ResultBytesPerPage)}/page  {cost.UploadBaseMs:F3} ms + {cost.UploadMsPerMiB:F3} ms/MiB");
        ImGuiTextSafe.Text(
            $"Backpress: {FormatBytes(chunkRenderer.CompletedMeshResultBytes)} completed  {FormatBytes(chunkRenderer.InFlightEstimatedMeshResultBytes)} predicted  {chunkRenderer.InFlightEstimatedMeshBuildMs:F2} worker-ms");
        ImGuiTextSafe.Text(
            $"Admission: build waits {chunkRenderer.MeshBuildAdmissionDeferrals:N0}  upload waits {chunkRenderer.MeshUploadAdmissionDeferrals:N0}  oversized {chunkRenderer.MeshOversizedUploadAdmissions:N0}");
        ImGuiTextSafe.Text(
            $"Versions:  allocated {MetricRegistry.Get(RenderMetrics.MeshVersionAllocated)}  free {MetricRegistry.Get(RenderMetrics.MeshVersionReleased)}");
        if (ImGui.Button("Reset mesh build profile")) chunkRenderer.ResetMeshProfile();
    }

    private static void DrawTerrainLod(ClientTerrainLodSnapshot? profile)
    {
        if (profile is not { } lod)
        {
            ImGuiTextSafe.TextDisabled("No distant terrain renderer is active.");
            return;
        }

        ImGuiTextSafe.Text(
            $"Work:      pending {lod.PendingColumns}  converting {lod.ConversionOwnedColumns}  uploads {lod.UploadsThisFrame}");
        ImGuiTextSafe.Text(
            $"LOD mesh:  owned {lod.MeshCompilationOwned}  coverage {lod.MeshCoverageQueued}/{lod.MeshCoverageCompleted} queued/done  refinement {lod.MeshRefinementQueued}/{lod.MeshRefinementCompleted} queued/done");
        ImGuiTextSafe.Text(
            $"LOD cost:  {lod.MeshCompilationMsPerKCell:F3} ms/Kcell  {FormatBytes((long)lod.MeshResultBytesPerKCell)}/Kcell  {lod.MeshUploadBaseMs:F3} ms + {lod.MeshUploadMsPerMiB:F3} ms/MiB");
        ImGuiTextSafe.Text(
            $"LOD pressure: {FormatBytes(lod.MeshCompletedResultBytes)} completed  {FormatBytes(lod.MeshPredictedResultBytes)} predicted  {lod.MeshPredictedCompilationMs:F2} worker-ms");
        ImGuiTextSafe.Text(
            $"LOD waits: compile {lod.MeshAdmissionDeferrals:N0}  upload {lod.MeshUploadAdmissionDeferrals:N0}  oversized {lod.MeshOversizedUploadAdmissions:N0}");
        ImGuiTextSafe.Text(
            $"Resident:  {lod.ResidentColumns:N0} columns  level-0 {lod.ExactVoxelLevelColumns:N0}  level-1 {lod.TransitionLevelColumns:N0}  {FormatBytes(lod.ResidentGpuBytes)} GPU estimate");
        ImGuiTextSafe.Text(
            $"Presented: solid {lod.PresentedColumns:N0}  translucent {lod.PresentedTranslucentColumns:N0} columns");
        ImGuiTextSafe.Text(
            $"LOD CPU:    solid {lod.SolidRenderCpuMs:F3} ms  translucent {lod.TranslucentRenderCpuMs:F3} ms");
        ImGuiTextSafe.Text(
            $"Handoff:   preparing {lod.HandoffPreparingColumns:N0}  overlap {lod.HandoffOverlapColumns:N0}  started {lod.HandoffsStarted:N0}  reversed {lod.HandoffReversals:N0}");
        ImGuiTextSafe.Text(
            $"LOD level: transitioning {lod.LevelTransitionColumns:N0}  started {lod.LevelTransitionsStarted:N0}  reversed {lod.LevelTransitionReversals:N0}");
        ImGuiTextSafe.Text(
            $"LOD seams: linked {lod.BoundaryLinkedColumns:N0}  pending {lod.BoundaryPendingColumns:N0}  refreshes {lod.BoundaryRefreshes:N0}  {FormatBytes(lod.ResidentBoundaryBytes)} CPU edges");
        ImGuiTextSafe.Text(
            $"LOD cache: entries {lod.CacheEntries:N0}  hits {lod.CacheHits:N0}  misses {lod.CacheMisses:N0}  writes {lod.CacheWrites:N0}/{lod.CacheWritesPending:N0} pending  drops {lod.CacheWriteDrops:N0}  errors {lod.CacheErrors:N0}  {FormatBytes(lod.CacheBytes)} disk");
        ImGuiTextSafe.Text(
            $"Resources: generation {lod.ResourceGeneration:N0}  reloads {lod.ResourceReloads:N0}  last reuse {lod.LastResourceReloadReusedColumns:N0} columns / {FormatBytes(lod.LastResourceReloadReusedGpuBytes)} GPU");
        ImGuiTextSafe.Text(
            $"Lifecycle: stale {lod.StaleResults:N0}  rejected {lod.RejectedAdmissions:N0}  evicted {lod.Evictions:N0}");
    }

    private static void DrawChunkLifecycle(ChunkRenderer chunkRenderer)
    {
        ImGuiTextSafe.Text(
            $"Pending:   foreground {chunkRenderer.ForegroundPending}  background {chunkRenderer.BackgroundPending}  oldest foreground {chunkRenderer.OldestForegroundAge}");
        ImGuiTextSafe.Text(
            $"Frontier:  leading queued {chunkRenderer.LeadingEdgeQueued}  pending {chunkRenderer.LeadingEdgePending}  eviction grace {chunkRenderer.EvictionGraceMeshCount}");
        ImGuiTextSafe.Text(
            $"Relight:   pending {chunkRenderer.LightRefreshPending}  completed {chunkRenderer.LightRefreshCompletedCount}");
        var lifecycle = chunkRenderer.MeshLifecycle;
        ImGuiTextSafe.Text(
            $"Critical:  completed {lifecycle.CriticalCompleted}  late {lifecycle.CriticalDeadlineMisses}  overdue {lifecycle.CriticalOverdue}");
        ImGuiTextSafe.Text(
            $"Cancelled: cooperative {lifecycle.CooperativeCancellations}  before {lifecycle.CancelledBeforeBuild}  during {lifecycle.CancelledDuringBuild}");
        ImGuiTextSafe.Text(
            $"Failures:  build {lifecycle.BuildFailures}  presentation regressions {chunkRenderer.PresentationRegressionCount}");
    }
}
