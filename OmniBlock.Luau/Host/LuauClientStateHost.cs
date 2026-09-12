using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OmniBlock.Luau.Host;

/// <summary>Read-only FFI facade for client readiness observed by automation scripts.</summary>
public static unsafe class LuauClientStateHost
{
    public const string Bootstrap = """
                                    OMNI.client = OMNI.client or {}
                                    OMNI.client.state = setmetatable({}, {
                                        __index = function(_, key)
                                            if key == "worldLoaded" then return __ClientState.worldLoaded() end
                                            if key == "playerReady" then return __ClientState.playerReady() end
                                            if key == "worldId" then return __ClientState.worldId() end
                                            if key == "debugOpen" then return __ClientState.debugOpen() end
                                            if key == "meshPending" then return __ClientState.meshPending() end
                                            if key == "meshCancelledCount" then return __ClientState.meshCancelledCount() end
                                            if key == "meshSupersededCount" then return __ClientState.meshSupersededCount() end
                                            if key == "meshBuildFailureCount" then return __ClientState.meshBuildFailureCount() end
                                            if key == "meshAwaitingUpload" then return __ClientState.meshAwaitingUpload() end
                                            if key == "meshAwaitingDraw" then return __ClientState.meshAwaitingDraw() end
                                            if key == "meshLeadingEdgeQueued" then return __ClientState.meshLeadingEdgeQueued() end
                                            if key == "meshLeadingEdgePending" then return __ClientState.meshLeadingEdgePending() end
                                            if key == "meshEvictionGraceCount" then return __ClientState.meshEvictionGraceCount() end
                                            if key == "meshCooperativeCancellationCount" then return __ClientState.meshCooperativeCancellationCount() end
                                            if key == "meshCriticalCompletedCount" then return __ClientState.meshCriticalCompletedCount() end
                                            if key == "meshCriticalDeadlineMissCount" then return __ClientState.meshCriticalDeadlineMissCount() end
                                            if key == "meshCriticalOverdueCount" then return __ClientState.meshCriticalOverdueCount() end
                                            if key == "meshRequestToGpuMs" then return __ClientState.meshRequestToGpuMs() end
                                            if key == "frameTimeMs" then return __ClientState.frameTimeMs() end
                                            if key == "meshSafetyLoadedColumns" then return __ClientState.meshSafetyLoadedColumns() end
                                            if key == "meshSafetyExpectedSections" then return __ClientState.meshSafetyExpectedSections() end
                                            if key == "meshSafetyHoles" then return __ClientState.meshSafetyHoles() end
                                            if key == "meshReadyRadius" then return __ClientState.meshReadyRadius() end
                                            if key == "residentMeshCount" then return __ClientState.residentMeshCount() end
                                            if key == "presentedMeshCount" then return __ClientState.presentedMeshCount() end
                                            if key == "foregroundPending" then return __ClientState.foregroundPending() end
                                            if key == "backgroundPending" then return __ClientState.backgroundPending() end
                                            if key == "lightRefreshPending" then return __ClientState.lightRefreshPending() end
                                            if key == "lightRefreshCompletedCount" then return __ClientState.lightRefreshCompletedCount() end
                                            if key == "geometryUploadsLastFrame" then return __ClientState.geometryUploadsLastFrame() end
                                            if key == "lightUploadsLastFrame" then return __ClientState.lightUploadsLastFrame() end
                                            if key == "solidDrawsLastFrame" then return __ClientState.solidDrawsLastFrame() end
                                            if key == "translucentDrawsLastFrame" then return __ClientState.translucentDrawsLastFrame() end
                                            if key == "residentSolidLayerCount" then return __ClientState.residentSolidLayerCount() end
                                            if key == "residentTranslucentLayerCount" then return __ClientState.residentTranslucentLayerCount() end
                                            if key == "visibilityCandidates" then return __ClientState.visibilityCandidates() end
                                            if key == "frustumTests" then return __ClientState.frustumTests() end
                                            if key == "portalVisited" then return __ClientState.portalVisited() end
                                            if key == "safetyRescued" then return __ClientState.safetyRescued() end
                                            if key == "renderDistance" then return __ClientState.renderDistance() end
                                            if key == "simulationDistance" then return __ClientState.simulationDistance() end
                                            if key == "presentedSolidLayerCount" then return __ClientState.presentedSolidLayerCount() end
                                            if key == "presentedTranslucentLayerCount" then return __ClientState.presentedTranslucentLayerCount() end
                                            if key == "emptyLayersSubmitted" then return __ClientState.emptyLayersSubmitted() end
                                            if key == "terrainDrawCalls" then return __ClientState.terrainDrawCalls() end
                                            if key == "terrainUniformEntries" then return __ClientState.terrainUniformEntries() end
                                            if key == "terrainSubmissionBatches" then return __ClientState.terrainSubmissionBatches() end
                                            if key == "terrainPipelineBinds" then return __ClientState.terrainPipelineBinds() end
                                            if key == "terrainTextureBinds" then return __ClientState.terrainTextureBinds() end
                                            if key == "terrainUniformArenaCapacity" then return __ClientState.terrainUniformArenaCapacity() end
                                            if key == "terrainUniformArenaGrowths" then return __ClientState.terrainUniformArenaGrowths() end
                                            if key == "findVisibleMs" then return __ClientState.findVisibleMs() end
                                            if key == "terrainSubmitCpuMs" then return __ClientState.terrainSubmitCpuMs() end
                                            if key == "entityLodObserved" or key == "entityLodIntendedImpostors" or
                                                key == "entityLodModelSubmissions" or key == "entityLodImpostorSubmissions" or
                                                key == "entityLodUnsupportedProvider" or key == "entityLodUnsupportedState" or
                                                key == "entityLodInvalidView" or key == "entityLodCapacityFallbacks" or
                                                key == "entityLodStateCount" or key == "entityLodResets" or
                                                key == "entityImpostorViews" or key == "entityImpostorReady" or
                                                key == "entityImpostorFailures" or key == "entityImpostorReplacements" or
                                                key == "entityImpostorPendingFallbacks" or key == "entityImpostorPoseMask" or
                                                key == "entityImpostorHurtSubmissions" or key == "webGpuErrorCount" or
                                                key == "entityImpostorMemoryHits" or key == "entityImpostorDiskHits" or
                                                key == "entityImpostorCacheMisses" or key == "entityImpostorCacheWrites" or
                                                key == "entityImpostorCacheErrors" or key == "entityImpostorCancellations" or
                                                key == "entityImpostorStaleResults" or key == "entityImpostorCapturedViews" or
                                                key == "entityImpostorMemoryBytes" or key == "entityImpostorReadbackPending" then
                                                return __ClientState.entityLod(key)
                                            end
                                            if key == "oldestForegroundAge" then return __ClientState.oldestForegroundAge() end
                                            if key == "presentationRegressionCount" then return __ClientState.presentationRegressionCount() end
                                            if key == "playerX" then return __ClientState.playerX() end
                                            if key == "playerY" then return __ClientState.playerY() end
                                            if key == "playerZ" then return __ClientState.playerZ() end
                                            return nil
                                        end,
                                        __newindex = function()
                                            error("OMNI.client.state is read-only", 2)
                                        end,
                                    })
                                    """;

    public static Func<bool>? WorldLoaded;
    public static Func<bool>? PlayerReady;
    public static Func<string?>? WorldId;
    public static Func<bool>? DebugOpen;
    public static Func<double>? MeshPending;
    public static Func<double>? MeshCancelledCount;
    public static Func<double>? MeshSupersededCount;
    public static Func<double>? MeshBuildFailureCount;
    public static Func<double>? MeshAwaitingUpload;
    public static Func<double>? MeshAwaitingDraw;
    public static Func<double>? MeshLeadingEdgeQueued;
    public static Func<double>? MeshLeadingEdgePending;
    public static Func<double>? MeshEvictionGraceCount;
    public static Func<double>? MeshCooperativeCancellationCount;
    public static Func<double>? MeshCriticalCompletedCount;
    public static Func<double>? MeshCriticalDeadlineMissCount;
    public static Func<double>? MeshCriticalOverdueCount;
    public static Func<double>? MeshRequestToGpuMs;
    public static Func<double>? FrameTimeMs;
    public static Func<double>? MeshSafetyLoadedColumns;
    public static Func<double>? MeshSafetyExpectedSections;
    public static Func<double>? MeshSafetyHoles;
    public static Func<double>? MeshReadyRadius;
    public static Func<double>? ResidentMeshCount;
    public static Func<double>? PresentedMeshCount;
    public static Func<double>? ForegroundPending;
    public static Func<double>? BackgroundPending;
    public static Func<double>? LightRefreshPending;
    public static Func<double>? LightRefreshCompletedCount;
    public static Func<double>? GeometryUploadsLastFrame;
    public static Func<double>? LightUploadsLastFrame;
    public static Func<double>? SolidDrawsLastFrame;
    public static Func<double>? TranslucentDrawsLastFrame;
    public static Func<double>? ResidentSolidLayerCount;
    public static Func<double>? ResidentTranslucentLayerCount;
    public static Func<double>? VisibilityCandidates;
    public static Func<double>? FrustumTests;
    public static Func<double>? PortalVisited;
    public static Func<double>? SafetyRescued;
    public static Func<double>? RenderDistance;
    public static Func<double>? SimulationDistance;
    public static Func<double>? PresentedSolidLayerCount;
    public static Func<double>? PresentedTranslucentLayerCount;
    public static Func<double>? EmptyLayersSubmitted;
    public static Func<double>? TerrainDrawCalls;
    public static Func<double>? TerrainUniformEntries;
    public static Func<double>? TerrainSubmissionBatches;
    public static Func<double>? TerrainPipelineBinds;
    public static Func<double>? TerrainTextureBinds;
    public static Func<double>? TerrainUniformArenaCapacity;
    public static Func<double>? TerrainUniformArenaGrowths;
    public static Func<double>? FindVisibleMs;
    public static Func<double>? TerrainSubmitCpuMs;
    public static Func<string, double>? EntityLodMetric;
    public static Func<double>? OldestForegroundAge;
    public static Func<double>? PresentationRegressionCount;
    public static Func<double>? PlayerX;
    public static Func<double>? PlayerY;
    public static Func<double>? PlayerZ;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 61);
        Add(l, "worldLoaded", &WorldLoadedClosure);
        Add(l, "playerReady", &PlayerReadyClosure);
        Add(l, "worldId", &WorldIdClosure);
        Add(l, "debugOpen", &DebugOpenClosure);
        Add(l, "meshPending", &MeshPendingClosure);
        Add(l, "meshCancelledCount", &MeshCancelledCountClosure);
        Add(l, "meshSupersededCount", &MeshSupersededCountClosure);
        Add(l, "meshBuildFailureCount", &MeshBuildFailureCountClosure);
        Add(l, "meshAwaitingUpload", &MeshAwaitingUploadClosure);
        Add(l, "meshAwaitingDraw", &MeshAwaitingDrawClosure);
        Add(l, "meshLeadingEdgeQueued", &MeshLeadingEdgeQueuedClosure);
        Add(l, "meshLeadingEdgePending", &MeshLeadingEdgePendingClosure);
        Add(l, "meshEvictionGraceCount", &MeshEvictionGraceCountClosure);
        Add(l, "meshCooperativeCancellationCount", &MeshCooperativeCancellationCountClosure);
        Add(l, "meshCriticalCompletedCount", &MeshCriticalCompletedCountClosure);
        Add(l, "meshCriticalDeadlineMissCount", &MeshCriticalDeadlineMissCountClosure);
        Add(l, "meshCriticalOverdueCount", &MeshCriticalOverdueCountClosure);
        Add(l, "meshRequestToGpuMs", &MeshRequestToGpuMsClosure);
        Add(l, "frameTimeMs", &FrameTimeMsClosure);
        Add(l, "meshSafetyLoadedColumns", &MeshSafetyLoadedColumnsClosure);
        Add(l, "meshSafetyExpectedSections", &MeshSafetyExpectedSectionsClosure);
        Add(l, "meshSafetyHoles", &MeshSafetyHolesClosure);
        Add(l, "meshReadyRadius", &MeshReadyRadiusClosure);
        Add(l, "residentMeshCount", &ResidentMeshCountClosure);
        Add(l, "presentedMeshCount", &PresentedMeshCountClosure);
        Add(l, "foregroundPending", &ForegroundPendingClosure);
        Add(l, "backgroundPending", &BackgroundPendingClosure);
        Add(l, "lightRefreshPending", &LightRefreshPendingClosure);
        Add(l, "lightRefreshCompletedCount", &LightRefreshCompletedCountClosure);
        Add(l, "geometryUploadsLastFrame", &GeometryUploadsLastFrameClosure);
        Add(l, "lightUploadsLastFrame", &LightUploadsLastFrameClosure);
        Add(l, "solidDrawsLastFrame", &SolidDrawsLastFrameClosure);
        Add(l, "translucentDrawsLastFrame", &TranslucentDrawsLastFrameClosure);
        Add(l, "residentSolidLayerCount", &ResidentSolidLayerCountClosure);
        Add(l, "residentTranslucentLayerCount", &ResidentTranslucentLayerCountClosure);
        Add(l, "visibilityCandidates", &VisibilityCandidatesClosure);
        Add(l, "frustumTests", &FrustumTestsClosure);
        Add(l, "portalVisited", &PortalVisitedClosure);
        Add(l, "safetyRescued", &SafetyRescuedClosure);
        Add(l, "renderDistance", &RenderDistanceClosure);
        Add(l, "simulationDistance", &SimulationDistanceClosure);
        Add(l, "presentedSolidLayerCount", &PresentedSolidLayerCountClosure);
        Add(l, "presentedTranslucentLayerCount", &PresentedTranslucentLayerCountClosure);
        Add(l, "emptyLayersSubmitted", &EmptyLayersSubmittedClosure);
        Add(l, "terrainDrawCalls", &TerrainDrawCallsClosure);
        Add(l, "terrainUniformEntries", &TerrainUniformEntriesClosure);
        Add(l, "terrainSubmissionBatches", &TerrainSubmissionBatchesClosure);
        Add(l, "terrainPipelineBinds", &TerrainPipelineBindsClosure);
        Add(l, "terrainTextureBinds", &TerrainTextureBindsClosure);
        Add(l, "terrainUniformArenaCapacity", &TerrainUniformArenaCapacityClosure);
        Add(l, "terrainUniformArenaGrowths", &TerrainUniformArenaGrowthsClosure);
        Add(l, "findVisibleMs", &FindVisibleMsClosure);
        Add(l, "terrainSubmitCpuMs", &TerrainSubmitCpuMsClosure);
        Add(l, "entityLod", &EntityLodClosure);
        Add(l, "oldestForegroundAge", &OldestForegroundAgeClosure);
        Add(l, "presentationRegressionCount", &PresentationRegressionCountClosure);
        Add(l, "playerX", &PlayerXClosure);
        Add(l, "playerY", &PlayerYClosure);
        Add(l, "playerZ", &PlayerZClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__ClientState");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__ClientState." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldLoadedClosure(IntPtr l)
    {
        LuauNative.lua_pushboolean(l, ReadBool(WorldLoaded) ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerReadyClosure(IntPtr l)
    {
        LuauNative.lua_pushboolean(l, ReadBool(PlayerReady) ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DebugOpenClosure(IntPtr l)
    {
        LuauNative.lua_pushboolean(l, ReadBool(DebugOpen) ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldIdClosure(IntPtr l)
    {
        string? id = null;
        try
        {
            id = WorldId?.Invoke();
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
        }

        if (id == null) LuauNative.lua_pushnil(l);
        else LuauNative.lua_pushstring(l, id);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshRequestToGpuMsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshRequestToGpuMs));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FrameTimeMsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(FrameTimeMs));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSafetyLoadedColumnsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSafetyLoadedColumns));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSafetyExpectedSectionsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSafetyExpectedSections));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSafetyHolesClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSafetyHoles));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshReadyRadiusClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshReadyRadius));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ResidentMeshCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(ResidentMeshCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PresentedMeshCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PresentedMeshCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ForegroundPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(ForegroundPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int BackgroundPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(BackgroundPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int LightRefreshPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(LightRefreshPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int LightRefreshCompletedCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(LightRefreshCompletedCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GeometryUploadsLastFrameClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(GeometryUploadsLastFrame));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int LightUploadsLastFrameClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(LightUploadsLastFrame));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SolidDrawsLastFrameClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(SolidDrawsLastFrame));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TranslucentDrawsLastFrameClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(TranslucentDrawsLastFrame));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ResidentSolidLayerCountClosure(IntPtr l) => PushNumber(l, ResidentSolidLayerCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ResidentTranslucentLayerCountClosure(IntPtr l) => PushNumber(l, ResidentTranslucentLayerCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int VisibilityCandidatesClosure(IntPtr l) => PushNumber(l, VisibilityCandidates);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FrustumTestsClosure(IntPtr l) => PushNumber(l, FrustumTests);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PortalVisitedClosure(IntPtr l) => PushNumber(l, PortalVisited);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SafetyRescuedClosure(IntPtr l) => PushNumber(l, SafetyRescued);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RenderDistanceClosure(IntPtr l) => PushNumber(l, RenderDistance);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SimulationDistanceClosure(IntPtr l) => PushNumber(l, SimulationDistance);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PresentedSolidLayerCountClosure(IntPtr l) => PushNumber(l, PresentedSolidLayerCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PresentedTranslucentLayerCountClosure(IntPtr l) => PushNumber(l, PresentedTranslucentLayerCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EmptyLayersSubmittedClosure(IntPtr l) => PushNumber(l, EmptyLayersSubmitted);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainDrawCallsClosure(IntPtr l) => PushNumber(l, TerrainDrawCalls);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainUniformEntriesClosure(IntPtr l) => PushNumber(l, TerrainUniformEntries);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainSubmissionBatchesClosure(IntPtr l) => PushNumber(l, TerrainSubmissionBatches);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainPipelineBindsClosure(IntPtr l) => PushNumber(l, TerrainPipelineBinds);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainTextureBindsClosure(IntPtr l) => PushNumber(l, TerrainTextureBinds);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainUniformArenaCapacityClosure(IntPtr l) => PushNumber(l, TerrainUniformArenaCapacity);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainUniformArenaGrowthsClosure(IntPtr l) => PushNumber(l, TerrainUniformArenaGrowths);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FindVisibleMsClosure(IntPtr l) => PushNumber(l, FindVisibleMs);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainSubmitCpuMsClosure(IntPtr l) => PushNumber(l, TerrainSubmitCpuMs);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EntityLodClosure(IntPtr l)
    {
        double value = 0;
        try
        {
            var pointer = LuauNative.lua_tolstring(l, 1, out var length);
            if (pointer != IntPtr.Zero)
                value = EntityLodMetric?.Invoke(Marshal.PtrToStringUTF8(pointer, (int)length) ?? "") ?? 0;
        }
        catch { }
        LuauNative.lua_pushnumber(l, value);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OldestForegroundAgeClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(OldestForegroundAge));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PresentationRegressionCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PresentationRegressionCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerXClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PlayerX));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerYClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PlayerY));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerZClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PlayerZ));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshCancelledCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshCancelledCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSupersededCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSupersededCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshBuildFailureCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshBuildFailureCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshAwaitingUploadClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshAwaitingUpload));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshAwaitingDrawClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshAwaitingDraw));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshLeadingEdgeQueuedClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshLeadingEdgeQueued));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshLeadingEdgePendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshLeadingEdgePending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshEvictionGraceCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshEvictionGraceCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshCooperativeCancellationCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshCooperativeCancellationCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshCriticalCompletedCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshCriticalCompletedCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshCriticalDeadlineMissCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshCriticalDeadlineMissCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshCriticalOverdueCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshCriticalOverdueCount));
        return 1;
    }

    private static bool ReadBool(Func<bool>? getter)
    {
        try
        {
            return getter?.Invoke() == true;
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return false;
        }
    }

    private static int PushNumber(IntPtr l, Func<double>? getter)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(getter));
        return 1;
    }

    private static double ReadNumber(Func<double>? getter)
    {
        try
        {
            return getter?.Invoke() ?? 0;
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return 0;
        }
    }
}
