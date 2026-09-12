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
        double residentSolidLayerCount = 0;
        double residentTranslucentLayerCount = 0;
        double visibilityCandidates = 0;
        double frustumTests = 0;
        double portalVisited = 0;
        double safetyRescued = 0;
        double renderDistance = 0;
        double simulationDistance = 0;
        double presentedSolidLayerCount = 0;
        double presentedTranslucentLayerCount = 0;
        double emptyLayersSubmitted = 0;
        double terrainDrawCalls = 0;
        double terrainUniformEntries = 0;
        double terrainSubmissionBatches = 0;
        double terrainPipelineBinds = 0;
        double terrainTextureBinds = 0;
        double terrainUniformArenaCapacity = 0;
        double terrainUniformArenaGrowths = 0;
        double findVisibleMs = 0;
        double terrainSubmitCpuMs = 0;
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
        LuauClientStateHost.ResidentSolidLayerCount = () => residentSolidLayerCount;
        LuauClientStateHost.ResidentTranslucentLayerCount = () => residentTranslucentLayerCount;
        LuauClientStateHost.VisibilityCandidates = () => visibilityCandidates;
        LuauClientStateHost.FrustumTests = () => frustumTests;
        LuauClientStateHost.PortalVisited = () => portalVisited;
        LuauClientStateHost.SafetyRescued = () => safetyRescued;
        LuauClientStateHost.RenderDistance = () => renderDistance;
        LuauClientStateHost.SimulationDistance = () => simulationDistance;
        LuauClientStateHost.PresentedSolidLayerCount = () => presentedSolidLayerCount;
        LuauClientStateHost.PresentedTranslucentLayerCount = () => presentedTranslucentLayerCount;
        LuauClientStateHost.EmptyLayersSubmitted = () => emptyLayersSubmitted;
        LuauClientStateHost.TerrainDrawCalls = () => terrainDrawCalls;
        LuauClientStateHost.TerrainUniformEntries = () => terrainUniformEntries;
        LuauClientStateHost.TerrainSubmissionBatches = () => terrainSubmissionBatches;
        LuauClientStateHost.TerrainPipelineBinds = () => terrainPipelineBinds;
        LuauClientStateHost.TerrainTextureBinds = () => terrainTextureBinds;
        LuauClientStateHost.TerrainUniformArenaCapacity = () => terrainUniformArenaCapacity;
        LuauClientStateHost.TerrainUniformArenaGrowths = () => terrainUniformArenaGrowths;
        LuauClientStateHost.FindVisibleMs = () => findVisibleMs;
        LuauClientStateHost.TerrainSubmitCpuMs = () => terrainSubmitCpuMs;
        LuauClientStateHost.EntityLodMetric = key => key == "entityLodIntendedImpostors" ? 12 : 0;

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
            AssertValue(state, "OMNI.client.state.visibilityCandidates", "0");
            AssertValue(state, "OMNI.client.state.findVisibleMs", "0");

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
            residentSolidLayerCount = 401;
            residentTranslucentLayerCount = 57;
            visibilityCandidates = 412;
            frustumTests = 527;
            portalVisited = 96;
            safetyRescued = 7;
            renderDistance = 24;
            simulationDistance = 8;
            presentedSolidLayerCount = 68;
            presentedTranslucentLayerCount = 14;
            emptyLayersSubmitted = 5;
            terrainDrawCalls = 82;
            terrainUniformEntries = 87;
            terrainSubmissionBatches = 2;
            terrainPipelineBinds = 2;
            terrainTextureBinds = 2;
            terrainUniformArenaCapacity = 1024;
            terrainUniformArenaGrowths = 3;
            findVisibleMs = 1.25;
            terrainSubmitCpuMs = 2.5;

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
            AssertValue(state, "OMNI.client.state.residentSolidLayerCount", "401");
            AssertValue(state, "OMNI.client.state.residentTranslucentLayerCount", "57");
            AssertValue(state, "OMNI.client.state.visibilityCandidates", "412");
            AssertValue(state, "OMNI.client.state.frustumTests", "527");
            AssertValue(state, "OMNI.client.state.portalVisited", "96");
            AssertValue(state, "OMNI.client.state.safetyRescued", "7");
            AssertValue(state, "OMNI.client.state.renderDistance", "24");
            AssertValue(state, "OMNI.client.state.simulationDistance", "8");
            AssertValue(state, "OMNI.client.state.presentedSolidLayerCount", "68");
            AssertValue(state, "OMNI.client.state.presentedTranslucentLayerCount", "14");
            AssertValue(state, "OMNI.client.state.emptyLayersSubmitted", "5");
            AssertValue(state, "OMNI.client.state.terrainDrawCalls", "82");
            AssertValue(state, "OMNI.client.state.terrainUniformEntries", "87");
            AssertValue(state, "OMNI.client.state.terrainSubmissionBatches", "2");
            AssertValue(state, "OMNI.client.state.terrainPipelineBinds", "2");
            AssertValue(state, "OMNI.client.state.terrainTextureBinds", "2");
            AssertValue(state, "OMNI.client.state.terrainUniformArenaCapacity", "1024");
            AssertValue(state, "OMNI.client.state.terrainUniformArenaGrowths", "3");
            AssertValue(state, "OMNI.client.state.findVisibleMs", "1.25");
            AssertValue(state, "OMNI.client.state.terrainSubmitCpuMs", "2.5");
            AssertValue(state, "OMNI.client.state.entityLodIntendedImpostors", "12");
            AssertValue(state, "OMNI.client.state.entityLodImpostorSubmissions", "0");
            Assert.False(state.TryExecute("OMNI.client.state.entityLodIntendedImpostors = 0", out var lodReadOnly));
            Assert.Contains("read-only", lodReadOnly);
            LuauClientStateHost.EntityLodMetric = null;
            AssertValue(state, "OMNI.client.state.entityLodIntendedImpostors", "0");
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
            LuauClientStateHost.ResidentSolidLayerCount = null;
            LuauClientStateHost.ResidentTranslucentLayerCount = null;
            LuauClientStateHost.VisibilityCandidates = null;
            LuauClientStateHost.FrustumTests = null;
            LuauClientStateHost.PortalVisited = null;
            LuauClientStateHost.SafetyRescued = null;
            LuauClientStateHost.RenderDistance = null;
            LuauClientStateHost.SimulationDistance = null;
            LuauClientStateHost.PresentedSolidLayerCount = null;
            LuauClientStateHost.PresentedTranslucentLayerCount = null;
            LuauClientStateHost.EmptyLayersSubmitted = null;
            LuauClientStateHost.TerrainDrawCalls = null;
            LuauClientStateHost.TerrainUniformEntries = null;
            LuauClientStateHost.TerrainSubmissionBatches = null;
            LuauClientStateHost.TerrainPipelineBinds = null;
            LuauClientStateHost.TerrainTextureBinds = null;
            LuauClientStateHost.TerrainUniformArenaCapacity = null;
            LuauClientStateHost.TerrainUniformArenaGrowths = null;
            LuauClientStateHost.FindVisibleMs = null;
            LuauClientStateHost.TerrainSubmitCpuMs = null;
            LuauClientStateHost.EntityLodMetric = null;
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
