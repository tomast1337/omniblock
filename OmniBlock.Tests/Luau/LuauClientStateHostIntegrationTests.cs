using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauClientStateHostIntegrationTests
{
    [SkippableFact]
    public void BootstrapExposesLiveReadOnlyReadinessState()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        var worldLoaded = false;
        var playerReady = false;
        string? worldId = null;
        var debugOpen = false;
        double meshPending = 0;
        double meshRequestToGpuMs = 0;
        double frameTimeMs = 0;
        double safetyLoaded = 0;
        double safetyExpected = 0;
        double safetyHoles = 0;
        double meshReadyRadius = 0;
        double residentMeshCount = 0;
        double presentedMeshCount = 0;
        double foregroundPending = 0;
        double backgroundPending = 0;
        double oldestForegroundAge = 0;
        double presentationRegressionCount = 0;
        double geometryUploadsLastFrame = 0;
        double lightUploadsLastFrame = 0;
        double solidDrawsLastFrame = 0;
        double translucentDrawsLastFrame = 0;
        LuauClientStateHost.WorldLoaded = () => worldLoaded;
        LuauClientStateHost.PlayerReady = () => playerReady;
        LuauClientStateHost.WorldId = () => worldId;
        LuauClientStateHost.DebugOpen = () => debugOpen;
        LuauClientStateHost.MeshPending = () => meshPending;
        LuauClientStateHost.MeshRequestToGpuMs = () => meshRequestToGpuMs;
        LuauClientStateHost.FrameTimeMs = () => frameTimeMs;
        LuauClientStateHost.MeshSafetyLoadedColumns = () => safetyLoaded;
        LuauClientStateHost.MeshSafetyExpectedSections = () => safetyExpected;
        LuauClientStateHost.MeshSafetyHoles = () => safetyHoles;
        LuauClientStateHost.MeshReadyRadius = () => meshReadyRadius;
        LuauClientStateHost.ResidentMeshCount = () => residentMeshCount;
        LuauClientStateHost.PresentedMeshCount = () => presentedMeshCount;
        LuauClientStateHost.ForegroundPending = () => foregroundPending;
        LuauClientStateHost.BackgroundPending = () => backgroundPending;
        LuauClientStateHost.OldestForegroundAge = () => oldestForegroundAge;
        LuauClientStateHost.PresentationRegressionCount = () => presentationRegressionCount;
        LuauClientStateHost.GeometryUploadsLastFrame = () => geometryUploadsLastFrame;
        LuauClientStateHost.LightUploadsLastFrame = () => lightUploadsLastFrame;
        LuauClientStateHost.SolidDrawsLastFrame = () => solidDrawsLastFrame;
        LuauClientStateHost.TranslucentDrawsLastFrame = () => translucentDrawsLastFrame;

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            LuauClientStateHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauClientStateHost.Bootstrap, out var bootstrapError), bootstrapError);

            AssertValue(state, "OMNI.client.state.worldLoaded", "false");
            AssertValue(state, "OMNI.client.state.playerReady", "false");
            AssertValue(state, "OMNI.client.state.worldId", "nil");
            AssertValue(state, "OMNI.client.state.debugOpen", "false");
            AssertValue(state, "OMNI.client.state.meshPending", "0");
            AssertValue(state, "OMNI.client.state.meshRequestToGpuMs", "0");
            AssertValue(state, "OMNI.client.state.frameTimeMs", "0");
            AssertValue(state, "OMNI.client.state.meshSafetyHoles", "0");
            AssertValue(state, "OMNI.client.state.residentMeshCount", "0");
            AssertValue(state, "OMNI.client.state.presentationRegressionCount", "0");
            AssertValue(state, "OMNI.client.state.geometryUploadsLastFrame", "0");

            worldLoaded = true;
            playerReady = true;
            worldId = "e2e-smoke";
            debugOpen = true;
            meshPending = 7;
            meshRequestToGpuMs = 12.5;
            frameTimeMs = 6.25;
            safetyLoaded = 29;
            safetyExpected = 232;
            safetyHoles = 3;
            meshReadyRadius = 2;
            residentMeshCount = 412;
            presentedMeshCount = 73;
            foregroundPending = 11;
            backgroundPending = 97;
            oldestForegroundAge = 24;
            presentationRegressionCount = 3;
            geometryUploadsLastFrame = 2;
            lightUploadsLastFrame = 4;
            solidDrawsLastFrame = 751;
            translucentDrawsLastFrame = 89;

            AssertValue(state, "OMNI.client.state.worldLoaded", "true");
            AssertValue(state, "OMNI.client.state.playerReady", "true");
            AssertValue(state, "OMNI.client.state.worldId", "e2e-smoke");
            AssertValue(state, "OMNI.client.state.debugOpen", "true");
            AssertValue(state, "OMNI.client.state.meshPending", "7");
            AssertValue(state, "OMNI.client.state.meshRequestToGpuMs", "12.5");
            AssertValue(state, "OMNI.client.state.frameTimeMs", "6.25");
            AssertValue(state, "OMNI.client.state.meshSafetyLoadedColumns", "29");
            AssertValue(state, "OMNI.client.state.meshSafetyExpectedSections", "232");
            AssertValue(state, "OMNI.client.state.meshSafetyHoles", "3");
            AssertValue(state, "OMNI.client.state.meshReadyRadius", "2");
            AssertValue(state, "OMNI.client.state.residentMeshCount", "412");
            AssertValue(state, "OMNI.client.state.presentedMeshCount", "73");
            AssertValue(state, "OMNI.client.state.foregroundPending", "11");
            AssertValue(state, "OMNI.client.state.backgroundPending", "97");
            AssertValue(state, "OMNI.client.state.oldestForegroundAge", "24");
            AssertValue(state, "OMNI.client.state.presentationRegressionCount", "3");
            AssertValue(state, "OMNI.client.state.geometryUploadsLastFrame", "2");
            AssertValue(state, "OMNI.client.state.lightUploadsLastFrame", "4");
            AssertValue(state, "OMNI.client.state.solidDrawsLastFrame", "751");
            AssertValue(state, "OMNI.client.state.translucentDrawsLastFrame", "89");
            Assert.False(state.TryExecute("OMNI.client.state.worldLoaded = false", out var readOnlyError));
            Assert.Contains("read-only", readOnlyError);
        }
        finally
        {
            LuauClientStateHost.WorldLoaded = null;
            LuauClientStateHost.PlayerReady = null;
            LuauClientStateHost.WorldId = null;
            LuauClientStateHost.DebugOpen = null;
            LuauClientStateHost.MeshPending = null;
            LuauClientStateHost.MeshRequestToGpuMs = null;
            LuauClientStateHost.FrameTimeMs = null;
            LuauClientStateHost.MeshSafetyLoadedColumns = null;
            LuauClientStateHost.MeshSafetyExpectedSections = null;
            LuauClientStateHost.MeshSafetyHoles = null;
            LuauClientStateHost.MeshReadyRadius = null;
            LuauClientStateHost.ResidentMeshCount = null;
            LuauClientStateHost.PresentedMeshCount = null;
            LuauClientStateHost.ForegroundPending = null;
            LuauClientStateHost.BackgroundPending = null;
            LuauClientStateHost.OldestForegroundAge = null;
            LuauClientStateHost.PresentationRegressionCount = null;
            LuauClientStateHost.GeometryUploadsLastFrame = null;
            LuauClientStateHost.LightUploadsLastFrame = null;
            LuauClientStateHost.SolidDrawsLastFrame = null;
            LuauClientStateHost.TranslucentDrawsLastFrame = null;
        }
    }

    [SkippableFact]
    public void Lifecycle_counters_are_live_read_only_and_safe_when_no_renderer_exists()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        double value = 0;
        LuauClientStateHost.MeshCancelledCount = () => value;
        LuauClientStateHost.MeshSupersededCount = () => value;
        LuauClientStateHost.MeshBuildFailureCount = () => value;
        LuauClientStateHost.MeshAwaitingUpload = () => value;
        LuauClientStateHost.MeshAwaitingDraw = () => value;
        LuauClientStateHost.MeshLeadingEdgeQueued = () => value;
        LuauClientStateHost.MeshLeadingEdgePending = () => value;
        LuauClientStateHost.MeshEvictionGraceCount = () => value;
        LuauClientStateHost.MeshCooperativeCancellationCount = () => value;
        LuauClientStateHost.MeshCriticalCompletedCount = () => value;
        LuauClientStateHost.MeshCriticalDeadlineMissCount = () => value;
        LuauClientStateHost.MeshCriticalOverdueCount = () => value;
        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var error), error);
            LuauClientStateHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauClientStateHost.Bootstrap, out error), error);
            AssertValue(state, "OMNI.client.state.meshCancelledCount", "0");
            AssertValue(state, "OMNI.client.state.meshSupersededCount", "0");
            AssertValue(state, "OMNI.client.state.meshBuildFailureCount", "0");
            AssertValue(state, "OMNI.client.state.meshAwaitingUpload", "0");
            AssertValue(state, "OMNI.client.state.meshAwaitingDraw", "0");
            AssertValue(state, "OMNI.client.state.meshLeadingEdgeQueued", "0");
            AssertValue(state, "OMNI.client.state.meshLeadingEdgePending", "0");
            AssertValue(state, "OMNI.client.state.meshEvictionGraceCount", "0");
            AssertValue(state, "OMNI.client.state.meshCooperativeCancellationCount", "0");
            AssertValue(state, "OMNI.client.state.meshCriticalCompletedCount", "0");
            AssertValue(state, "OMNI.client.state.meshCriticalDeadlineMissCount", "0");
            AssertValue(state, "OMNI.client.state.meshCriticalOverdueCount", "0");
            value = 7;
            AssertValue(state, "OMNI.client.state.meshCancelledCount", "7");
            AssertValue(state, "OMNI.client.state.meshSupersededCount", "7");
            AssertValue(state, "OMNI.client.state.meshBuildFailureCount", "7");
            AssertValue(state, "OMNI.client.state.meshAwaitingUpload", "7");
            AssertValue(state, "OMNI.client.state.meshAwaitingDraw", "7");
            AssertValue(state, "OMNI.client.state.meshLeadingEdgeQueued", "7");
            AssertValue(state, "OMNI.client.state.meshLeadingEdgePending", "7");
            AssertValue(state, "OMNI.client.state.meshEvictionGraceCount", "7");
            AssertValue(state, "OMNI.client.state.meshCooperativeCancellationCount", "7");
            AssertValue(state, "OMNI.client.state.meshCriticalCompletedCount", "7");
            AssertValue(state, "OMNI.client.state.meshCriticalDeadlineMissCount", "7");
            AssertValue(state, "OMNI.client.state.meshCriticalOverdueCount", "7");
            Assert.False(state.TryExecute("OMNI.client.state.meshCancelledCount = 0", out error));
            Assert.Contains("read-only", error);
            LuauClientStateHost.MeshAwaitingUpload = () => throw new InvalidOperationException();
            AssertValue(state, "OMNI.client.state.meshAwaitingUpload", "0");
        }
        finally
        {
            LuauClientStateHost.MeshCancelledCount = null;
            LuauClientStateHost.MeshSupersededCount = null;
            LuauClientStateHost.MeshBuildFailureCount = null;
            LuauClientStateHost.MeshAwaitingUpload = null;
            LuauClientStateHost.MeshAwaitingDraw = null;
            LuauClientStateHost.MeshLeadingEdgeQueued = null;
            LuauClientStateHost.MeshLeadingEdgePending = null;
            LuauClientStateHost.MeshEvictionGraceCount = null;
            LuauClientStateHost.MeshCooperativeCancellationCount = null;
            LuauClientStateHost.MeshCriticalCompletedCount = null;
            LuauClientStateHost.MeshCriticalDeadlineMissCount = null;
            LuauClientStateHost.MeshCriticalOverdueCount = null;
        }
        AssertValue(state, "OMNI.client.state.meshCancelledCount", "0");
    }

    private static void AssertValue(LuauState state, string expression, string expected)
    {
        Assert.True(state.TryExecute(expression, out var value), value);
        Assert.Equal(expected, value);
    }
}
