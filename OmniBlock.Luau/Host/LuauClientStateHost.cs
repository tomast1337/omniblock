using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;

namespace OmniBlock.Luau.Host;

/// <summary>Read-only FFI facade for client readiness observed by automation scripts.</summary>
public static unsafe class LuauClientStateHost
{
    public const string Bootstrap = """
                                    OMNI.client = OMNI.client or {}
                                    OMNI.client.state = setmetatable({}, {
                                        __index = function(_, key)
                                            return __ClientState.get(key)
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
    public static Func<double>? VisibilityReuseFrames;
    public static Func<double>? VisibilitySynchronousFrames;
    public static Func<double>? VisibilityBuilds;
    public static Func<double>? VisibilityBuildCancellations;
    public static Func<double>? VisibilityStaleResults;
    public static Func<double>? VisibilityBuildInFlight;
    public static Func<double>? VisibilityWorkerCandidates;
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
    public static Func<string, double>? TerrainLodMetric;
    public static Func<string, double>? EntityLodMetric;
    public static Func<double>? OldestForegroundAge;
    public static Func<double>? PresentationRegressionCount;
    public static Func<double>? PlayerX;
    public static Func<double>? PlayerY;
    public static Func<double>? PlayerZ;

    private static readonly Dictionary<string, Action<IntPtr>> StateReaders = new(StringComparer.Ordinal)
    {
        ["worldLoaded"] = static l => PushBool(l, WorldLoaded),
        ["playerReady"] = static l => PushBool(l, PlayerReady),
        ["worldId"] = static l => PushString(l, WorldId),
        ["debugOpen"] = static l => PushBool(l, DebugOpen),
        ["meshPending"] = static l => PushNumber(l, MeshPending),
        ["meshCancelledCount"] = static l => PushNumber(l, MeshCancelledCount),
        ["meshSupersededCount"] = static l => PushNumber(l, MeshSupersededCount),
        ["meshBuildFailureCount"] = static l => PushNumber(l, MeshBuildFailureCount),
        ["meshAwaitingUpload"] = static l => PushNumber(l, MeshAwaitingUpload),
        ["meshAwaitingDraw"] = static l => PushNumber(l, MeshAwaitingDraw),
        ["meshLeadingEdgeQueued"] = static l => PushNumber(l, MeshLeadingEdgeQueued),
        ["meshLeadingEdgePending"] = static l => PushNumber(l, MeshLeadingEdgePending),
        ["meshEvictionGraceCount"] = static l => PushNumber(l, MeshEvictionGraceCount),
        ["meshCooperativeCancellationCount"] = static l => PushNumber(l, MeshCooperativeCancellationCount),
        ["meshCriticalCompletedCount"] = static l => PushNumber(l, MeshCriticalCompletedCount),
        ["meshCriticalDeadlineMissCount"] = static l => PushNumber(l, MeshCriticalDeadlineMissCount),
        ["meshCriticalOverdueCount"] = static l => PushNumber(l, MeshCriticalOverdueCount),
        ["meshRequestToGpuMs"] = static l => PushNumber(l, MeshRequestToGpuMs),
        ["frameTimeMs"] = static l => PushNumber(l, FrameTimeMs),
        ["meshSafetyLoadedColumns"] = static l => PushNumber(l, MeshSafetyLoadedColumns),
        ["meshSafetyExpectedSections"] = static l => PushNumber(l, MeshSafetyExpectedSections),
        ["meshSafetyHoles"] = static l => PushNumber(l, MeshSafetyHoles),
        ["meshReadyRadius"] = static l => PushNumber(l, MeshReadyRadius),
        ["residentMeshCount"] = static l => PushNumber(l, ResidentMeshCount),
        ["presentedMeshCount"] = static l => PushNumber(l, PresentedMeshCount),
        ["foregroundPending"] = static l => PushNumber(l, ForegroundPending),
        ["backgroundPending"] = static l => PushNumber(l, BackgroundPending),
        ["lightRefreshPending"] = static l => PushNumber(l, LightRefreshPending),
        ["lightRefreshCompletedCount"] = static l => PushNumber(l, LightRefreshCompletedCount),
        ["geometryUploadsLastFrame"] = static l => PushNumber(l, GeometryUploadsLastFrame),
        ["lightUploadsLastFrame"] = static l => PushNumber(l, LightUploadsLastFrame),
        ["solidDrawsLastFrame"] = static l => PushNumber(l, SolidDrawsLastFrame),
        ["translucentDrawsLastFrame"] = static l => PushNumber(l, TranslucentDrawsLastFrame),
        ["residentSolidLayerCount"] = static l => PushNumber(l, ResidentSolidLayerCount),
        ["residentTranslucentLayerCount"] = static l => PushNumber(l, ResidentTranslucentLayerCount),
        ["visibilityCandidates"] = static l => PushNumber(l, VisibilityCandidates),
        ["visibilityReuseFrames"] = static l => PushNumber(l, VisibilityReuseFrames),
        ["visibilitySynchronousFrames"] = static l => PushNumber(l, VisibilitySynchronousFrames),
        ["visibilityBuilds"] = static l => PushNumber(l, VisibilityBuilds),
        ["visibilityBuildCancellations"] = static l => PushNumber(l, VisibilityBuildCancellations),
        ["visibilityStaleResults"] = static l => PushNumber(l, VisibilityStaleResults),
        ["visibilityBuildInFlight"] = static l => PushNumber(l, VisibilityBuildInFlight),
        ["visibilityWorkerCandidates"] = static l => PushNumber(l, VisibilityWorkerCandidates),
        ["frustumTests"] = static l => PushNumber(l, FrustumTests),
        ["portalVisited"] = static l => PushNumber(l, PortalVisited),
        ["safetyRescued"] = static l => PushNumber(l, SafetyRescued),
        ["renderDistance"] = static l => PushNumber(l, RenderDistance),
        ["simulationDistance"] = static l => PushNumber(l, SimulationDistance),
        ["presentedSolidLayerCount"] = static l => PushNumber(l, PresentedSolidLayerCount),
        ["presentedTranslucentLayerCount"] = static l => PushNumber(l, PresentedTranslucentLayerCount),
        ["emptyLayersSubmitted"] = static l => PushNumber(l, EmptyLayersSubmitted),
        ["terrainDrawCalls"] = static l => PushNumber(l, TerrainDrawCalls),
        ["terrainUniformEntries"] = static l => PushNumber(l, TerrainUniformEntries),
        ["terrainSubmissionBatches"] = static l => PushNumber(l, TerrainSubmissionBatches),
        ["terrainPipelineBinds"] = static l => PushNumber(l, TerrainPipelineBinds),
        ["terrainTextureBinds"] = static l => PushNumber(l, TerrainTextureBinds),
        ["terrainUniformArenaCapacity"] = static l => PushNumber(l, TerrainUniformArenaCapacity),
        ["terrainUniformArenaGrowths"] = static l => PushNumber(l, TerrainUniformArenaGrowths),
        ["findVisibleMs"] = static l => PushNumber(l, FindVisibleMs),
        ["terrainSubmitCpuMs"] = static l => PushNumber(l, TerrainSubmitCpuMs),
        ["oldestForegroundAge"] = static l => PushNumber(l, OldestForegroundAge),
        ["presentationRegressionCount"] = static l => PushNumber(l, PresentationRegressionCount),
        ["playerX"] = static l => PushNumber(l, PlayerX),
        ["playerY"] = static l => PushNumber(l, PlayerY),
        ["playerZ"] = static l => PushNumber(l, PlayerZ)
    };

    private static readonly HashSet<string> TerrainLodKeys = new(StringComparer.Ordinal)
    {
        "terrainLodPending", "terrainLodConverting", "terrainLodResident",
        "terrainLodLevel0Resident", "terrainLodLevel1Resident", "terrainLodPresented",
        "terrainLodTranslucentPresented", "terrainLodHandoffPreparing", "terrainLodHandoffOverlap",
        "terrainLodHandoffsStarted", "terrainLodHandoffReversals", "terrainLodLevelTransitions",
        "terrainLodLevelTransitionsStarted", "terrainLodLevelTransitionReversals",
        "terrainLodBoundaryLinked", "terrainLodBoundaryPending", "terrainLodBoundaryRefreshes",
        "terrainLodBoundaryBytes", "terrainLodCacheHits", "terrainLodCacheMisses",
        "terrainLodResourceGeneration", "terrainLodResourceReloads",
        "terrainLodResourceReusedColumns", "terrainLodResourceReusedGpuBytes",
        "terrainLodSolidCpuMs", "terrainLodTranslucentCpuMs", "terrainLodCacheBytes",
        "terrainLodMeshOwned", "terrainLodMeshCoverageQueued", "terrainLodMeshRefinementQueued",
        "terrainLodMeshCoverageCompleted", "terrainLodMeshRefinementCompleted",
        "terrainLodMeshCompletedBytes", "terrainLodMeshPredictedBytes", "terrainLodMeshPredictedMs",
        "terrainLodMeshAdmissionDeferrals", "terrainLodMeshUploadDeferrals",
        "terrainLodMeshOversizedUploads", "terrainLodMeshCompilationSamples",
        "terrainLodMeshUploadSamples", "terrainLodMeshCompilationMsPerKCell",
        "terrainLodMeshResultBytesPerKCell", "terrainLodMeshUploadBaseMs",
        "terrainLodMeshUploadMsPerMiB", "terrainLodSpatialComplete", "terrainLodSpatialSelected",
        "terrainLodSpatialParentFallbacks", "terrainLodSpatialMissingGroups",
        "terrainLodSpatialGpuResident", "terrainLodSpatialHighestGpuLevel",
        "terrainLodSpatialCpuTiles", "terrainLodSpatialCurrentTiles", "terrainLodSpatialMeshPending",
        "terrainLodSpatialMeshQueued", "terrainLodSpatialMeshRunning", "terrainLodSpatialMeshReady",
        "terrainLodSpatialMeshCancelled", "terrainLodSpatialMeshOverBudget",
        "terrainLodSpatialMeshOldestQueuedMs", "terrainLodSpatialMeshCompleted",
        "terrainLodSpatialMeshCompilationTotalMs", "terrainLodSpatialMeshCompilationMaxMs",
        "terrainLodSpatialMeshPeakQueued", "terrainLodSpatialMeshPeakRunning",
        "terrainLodSpatialMeshPeakCompleted", "terrainLodSpatialMeshReductionMs",
        "terrainLodSpatialMeshFaceEmissionMs", "terrainLodSpatialMeshFlatteningMs",
        "terrainLodSpatialMeshCoalescingMs", "terrainLodSpatialMeshSourceColumns",
        "terrainLodSpatialMeshSourceSpans", "terrainLodSpatialMeshConstructionPages",
        "terrainLodSpatialSeamDesired", "terrainLodSpatialSeamGpuResident",
        "terrainLodSpatialSeamQueued", "terrainLodSpatialSeamRunning", "terrainLodSpatialSeamReady",
        "terrainLodSpatialSeamCancelled", "terrainLodSpatialSeamOverBudget",
        "terrainLodSpatialSeamOldestQueuedMs",
        "terrainLodSpatialSubmissionReady", "terrainLodSpatialAuthoritativeTiles",
        "terrainLodSpatialHighestAuthoritativeLevel", "terrainLodSpatialSolidPages",
        "terrainLodSpatialTranslucentPages", "terrainLodSpatialGpuBytes", "terrainLodSpatialPinned",
        "terrainLodSpatialGpuEvictions", "terrainLodSpatialCpuEvictions",
        "terrainLodCoarseSourceUnavailable", "terrainLodCoarseBuilding",
        "terrainLodCoarseTransportPending", "terrainLodCoarseGpuPending", "terrainLodCoarseReady",
        "terrainLodCoarseAwaitingRequest", "terrainLodCoarseFrontierUnknown",
        "terrainLodCoarseComplete", "terrainLodCoarseRetainingPrevious", "terrainLodColdCoverMs",
        "terrainLodFirstCompleteHorizonMs", "terrainLodRefinementMs",
        "terrainLodConvergenceGeneration", "terrainLodFirstRequestMs",
        "terrainLodFirstSourceTileMs", "terrainLodSourceCompleteMs",
        "terrainLodFirstBodyUploadMs", "terrainLodBodiesCompleteMs",
        "terrainLodFirstSeamUploadMs", "terrainLodSeamsCompleteMs",
        "terrainLodPublicationMs", "terrainLodBodyUploads",
        "terrainLodBodyUploadBytes", "terrainLodBodyInstallMs",
        "terrainLodSeamUploads", "terrainLodSeamUploadBytes",
        "terrainLodSeamInstallMs", "terrainCoverageReady",
        "terrainCoverageExpected", "terrainCoverageCovered", "terrainCoverageExact",
        "terrainCoverageColumnLod", "terrainCoverageSpatial", "terrainCoverageTransitions",
        "terrainCoverageHoles", "terrainCoverageMissingChunkData",
        "terrainCoverageMissingExactMeshes", "terrainCoverageMissingPresentation",
        "terrainCoverageOverlaps", "terrainCoverageExpectedSeams",
        "terrainCoverageMissingSeams", "terrainCoveragePendingSeams", "terrainCoverageUnexpectedSeams",
        "terrainCoverageFailureKind", "terrainCoverageFailureX", "terrainCoverageFailureZ",
        "terrainLodRemoteRequests", "terrainLodRemoteTiles", "terrainLodRemoteBytes",
        "terrainLodRemotePending", "terrainLodRemoteMissing", "terrainLodRemoteDeferred",
        "terrainLodRemoteCoverageRequired", "terrainLodRemoteCoverageAvailable",
        "terrainLodRemoteCoverageInFlight", "terrainLodRemoteCoveragePending",
        "terrainLodRemoteCoverageMissing", "terrainLodRemoteCoverageDeferred",
        "terrainLodRemoteCoverageComplete", "terrainLodNetworkTilesReceived",
        "terrainLodNetworkTilesAdmitted", "terrainLodNetworkTileQueue",
        "terrainLodNetworkTileQueuePeak", "terrainLodTransportQueue",
        "terrainLodTransportQueuePeak", "terrainLodIdentityReady",
        "terrainLodIdentityMismatches", "terrainLodIdentityRejectedMessages", "clientWorkingSetBytes",
        "terrainLodUploads", "terrainLodGpuBytes", "terrainLodStaleResults", "terrainLodRejected",
        "terrainLodEvictions"
    };

    private static readonly HashSet<string> EntityLodKeys = new(StringComparer.Ordinal)
    {
        "entityClientResident", "entityPresented", "entityHidden", "entityLodObserved",
        "entityLodIntendedImpostors", "entityLodModelSubmissions", "entityLodImpostorSubmissions",
        "entityLodUnsupportedProvider", "entityLodUnsupportedState", "entityLodInvalidView",
        "entityLodCapacityFallbacks", "entityLodStateCount", "entityLodResets",
        "entityLodTransitions", "entityImpostorViews", "entityImpostorReady",
        "entityImpostorFailures", "entityImpostorReplacements", "entityImpostorPendingFallbacks",
        "entityImpostorPoseMask", "entityImpostorHurtSubmissions", "entityImpostorOverlaySubmissions",
        "webGpuErrorCount", "entityImpostorMemoryHits", "entityImpostorDiskHits",
        "entityImpostorCacheMisses", "entityImpostorCacheWrites", "entityImpostorCacheErrors",
        "entityImpostorCancellations", "entityImpostorStaleResults", "entityImpostorCapturedViews",
        "entityImpostorMemoryBytes", "entityImpostorReadbackPending", "entityImpostorInvalidations",
        "entityImpostorBakeQueueAgeMs", "entityImpostorLastBakeMs", "entityImpostorAverageBakeMs",
        "entityImpostorCaptureCpuMs", "entityImpostorResidentGpuBytes", "entityImpostorStagingBytes",
        "entityImpostorDrawBatches", "entityImpostorResidentAtlases", "entityDistanceDespawnVisuals",
        "entityDistanceDespawnPresentationCount"
    };

    /// <summary>The state properties exposed to scripts, for editor definitions and completion.</summary>
    public static IReadOnlyList<(string Name, string LuauType)> GetStateDefinition()
    {
        List<(string Name, string LuauType)> properties = [];
        foreach (var key in StateReaders.Keys)
        {
            var fieldName = char.ToUpperInvariant(key[0]) + key[1..];
            var field = typeof(LuauClientStateHost).GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"No state getter exists for '{key}'.");
            var type = field.FieldType == typeof(Func<bool>) ? "boolean"
                : field.FieldType == typeof(Func<double>) ? "number"
                : field.FieldType == typeof(Func<string>) ? "string?"
                : throw new InvalidOperationException($"Unsupported state getter type for '{key}'.");
            properties.Add((key, type));
        }

        properties.AddRange(TerrainLodKeys.Select(key => (key, "number")));
        properties.AddRange(EntityLodKeys.Select(key => (key, "number")));
        return properties.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
    }

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 1);
        Add(l, "get", &GetClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__ClientState");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GetClosure(IntPtr l)
    {
        try
        {
            var key = ReadStringArgument(l, 1);
            if (key is null) return PushNil(l);

            if (StateReaders.TryGetValue(key, out var reader))
            {
                reader(l);
                return 1;
            }

            if (TerrainLodKeys.Contains(key))
            {
                PushMetric(l, TerrainLodMetric, key);
                return 1;
            }

            if (EntityLodKeys.Contains(key))
            {
                PushMetric(l, EntityLodMetric, key);
                return 1;
            }

            return PushNil(l);
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return PushNil(l);
        }
    }

    private static string? ReadStringArgument(IntPtr l, int index)
    {
        var pointer = LuauNative.lua_tolstring(l, index, out var length);
        return pointer == IntPtr.Zero
            ? null
            : Marshal.PtrToStringUTF8(pointer, checked((int)length));
    }

    private static void PushNumber(IntPtr l, Func<double>? getter) =>
        LuauNative.lua_pushnumber(l, Read(getter, 0d));

    private static void PushBool(IntPtr l, Func<bool>? getter) =>
        LuauNative.lua_pushboolean(l, Read(getter, false) ? 1 : 0);

    private static void PushString(IntPtr l, Func<string?>? getter)
    {
        var value = Read(getter, default(string));
        if (value is null) LuauNative.lua_pushnil(l);
        else LuauNative.lua_pushstring(l, value);
    }

    private static void PushMetric(IntPtr l, Func<string, double>? getter, string key)
    {
        double value = 0;
        try
        {
            value = getter?.Invoke(key) ?? 0;
        }
        catch
        {
            // Preserve the automation API's historical zero fallback without allocating a closure.
        }

        LuauNative.lua_pushnumber(l, value);
    }

    private static T Read<T>(Func<T>? getter, T fallback)
    {
        try
        {
            return getter is null ? fallback : getter();
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return fallback;
        }
    }

    private static int PushNil(IntPtr l)
    {
        LuauNative.lua_pushnil(l);
        return 1;
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__ClientState." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }
}
