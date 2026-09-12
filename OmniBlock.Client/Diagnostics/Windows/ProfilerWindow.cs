using Hexa.NET.ImGui;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Diagnostics;
using OmniBlock.Profiling;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class ProfilerWindow(DebugWindowContext ctx) : DebugWindow
{
    public override string Title => "Profiler";
    public override DebugDock DefaultDock => DebugDock.Right;

    protected override void OnDraw()
    {
        if (ImGui.CollapsingHeader("Frame timings", ImGuiTreeNodeFlags.DefaultOpen))
            ProfilerRenderer.DrawContents();

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
            $"Fallback:   {profile.DisconnectedSeeds} disconnected seeds  {profile.SafetyRescued} safety rescued  {profile.EmptyLayersSubmitted} empty submitted");
        ImGuiTextSafe.Text(
            $"Submission: {profile.TerrainDrawCalls} draws  {profile.TerrainUniformEntries} uniforms");
        DrawTiming("Find visible", profile.FindVisible);
        DrawTiming("Terrain submit", profile.TerrainSubmit);
    }

    private static void DrawTiming(
        string label,
        FrameTimingSnapshot timing)
    {
        ImGuiTextSafe.Text(
            $"{label}: {timing.LastMs:F3} ms  avg {timing.AverageMs:F3}  p50 {timing.P50Ms:F3}  p95 {timing.P95Ms:F3}  max {timing.MaxMs:F3}");
    }

    private static void DrawChunkMeshConstruction(ChunkRenderer chunkRenderer)
    {
        var mesh = chunkRenderer.MeshProfile;
        ImGuiTextSafe.Text(
            $"Workers:   {mesh.Workers}  queued {mesh.Queued}  outstanding {mesh.Outstanding}");
        ImGuiTextSafe.Text(
            $"Results:   critical {mesh.CriticalResults}  foreground {mesh.ForegroundResults}  background {mesh.BackgroundResults}");
        ImGuiTextSafe.Text($"Built:     {mesh.Meshes:N0}");
        ImGuiTextSafe.Text($"Snapshot:  {mesh.SnapshotMs:F3} ms avg");
        ImGuiTextSafe.Text($"Queue wait:{mesh.QueueWaitMs,7:F3} ms avg");
        ImGuiTextSafe.Text($"Classify:  {mesh.ClassificationMs:F3} ms avg");
        ImGuiTextSafe.Text($"Geometry:  {mesh.GeometryMs:F3} ms avg");
        ImGuiTextSafe.Text($"Visibility:{mesh.VisibilityMs,7:F3} ms avg");
        ImGuiTextSafe.Text($"Generate:  {mesh.GenerationMs:F3} ms avg");
        ImGuiTextSafe.Text($"Upload:    {mesh.UploadMs:F3} ms avg");
        ImGuiTextSafe.Text($"Done->GPU: {mesh.FinishedToUploadMs:F3} ms avg");
        ImGuiTextSafe.Text($"Request->GPU: {mesh.RequestToUploadMs:F3} ms avg");
        ImGuiTextSafe.Text(
            $"Versions:  allocated {MetricRegistry.Get(RenderMetrics.MeshVersionAllocated)}  free {MetricRegistry.Get(RenderMetrics.MeshVersionReleased)}");
        if (ImGui.Button("Reset mesh build profile")) chunkRenderer.ResetMeshProfile();
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
