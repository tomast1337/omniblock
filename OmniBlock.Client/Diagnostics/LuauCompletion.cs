using System.Text.RegularExpressions;

namespace OmniBlock.Client.Diagnostics;

internal sealed partial class LuauCompletion
{
    private static readonly string[] s_globals =
    [
        "and", "break", "continue", "do", "else", "elseif", "end", "export", "false",
        "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return",
        "then", "true", "type", "until", "while",
        "assert", "bit32", "buffer", "collectgarbage", "coroutine", "debug", "OMNI",
        "error", "gcinfo", "getmetatable", "ipairs", "math", "next", "pairs", "pcall",
        "print", "rawequal", "rawget", "rawlen", "rawset", "select", "setmetatable",
        "string", "table", "tonumber", "tostring", "typeof", "utf8", "xpcall"
    ];

    private static readonly string[] s_omniMembers = ["client", "config", "environment", "has", "run", "test", "ui", "wait", "waitUntil", "worldgen"];
    private static readonly string[] s_clientMembers = ["state", "worlds"];
    private static readonly string[] s_entityLodMembers =
    [
        "entityLodObserved", "entityLodIntendedImpostors", "entityLodModelSubmissions",
        "entityLodImpostorSubmissions", "entityLodUnsupportedProvider", "entityLodUnsupportedState",
        "entityLodInvalidView", "entityLodCapacityFallbacks", "entityLodTransitions", "entityLodStateCount", "entityLodResets",
        "entityImpostorViews", "entityImpostorReady", "entityImpostorFailures", "entityImpostorReplacements", "entityImpostorPendingFallbacks",
        "entityImpostorPoseMask", "entityImpostorHurtSubmissions", "entityImpostorOverlaySubmissions", "webGpuErrorCount",
        "entityImpostorMemoryHits", "entityImpostorDiskHits", "entityImpostorCacheMisses", "entityImpostorCacheWrites",
        "entityImpostorCacheErrors", "entityImpostorCancellations", "entityImpostorStaleResults", "entityImpostorCapturedViews", "entityImpostorMemoryBytes", "entityImpostorReadbackPending",
        "entityImpostorInvalidations", "entityImpostorBakeQueueAgeMs", "entityImpostorLastBakeMs", "entityImpostorAverageBakeMs", "entityImpostorCaptureCpuMs",
        "entityImpostorResidentGpuBytes", "entityImpostorStagingBytes", "entityImpostorDrawBatches", "entityImpostorResidentAtlases"
    ];

    private static readonly string[] s_clientStateMembers =
        ["backgroundPending", "clientWorkingSetBytes", "debugOpen", "emptyLayersSubmitted", "findVisibleMs", "foregroundPending", "frameTimeMs", "frustumTests", "geometryUploadsLastFrame", "lightRefreshCompletedCount", "lightRefreshPending", "lightUploadsLastFrame", "meshAwaitingDraw", "meshAwaitingUpload", "meshBuildFailureCount", "meshCancelledCount", "meshCooperativeCancellationCount", "meshCriticalCompletedCount", "meshCriticalDeadlineMissCount", "meshCriticalOverdueCount", "meshEvictionGraceCount", "meshLeadingEdgePending", "meshLeadingEdgeQueued", "meshPending", "meshReadyRadius", "meshRequestToGpuMs", "meshSafetyExpectedSections", "meshSafetyHoles", "meshSafetyLoadedColumns", "meshSupersededCount", "oldestForegroundAge", "playerReady", "playerX", "playerY", "playerZ", "portalVisited", "presentationRegressionCount", "presentedMeshCount", "presentedSolidLayerCount", "presentedTranslucentLayerCount", "renderDistance", "residentMeshCount", "residentSolidLayerCount", "residentTranslucentLayerCount", "safetyRescued", "simulationDistance", "solidDrawsLastFrame", "terrainCoverageReady", "terrainCoverageExpected", "terrainCoverageCovered", "terrainCoverageExact", "terrainCoverageColumnLod", "terrainCoverageSpatial", "terrainCoverageTransitions", "terrainCoverageHoles", "terrainCoverageOverlaps", "terrainCoverageExpectedSeams", "terrainCoverageMissingSeams", "terrainCoverageUnexpectedSeams", "terrainCoverageFailureKind", "terrainCoverageFailureX", "terrainCoverageFailureZ", "terrainDrawCalls", "terrainLodBoundaryBytes", "terrainLodBoundaryLinked", "terrainLodBoundaryPending", "terrainLodBoundaryRefreshes", "terrainLodCacheBytes", "terrainLodCacheHits", "terrainLodCacheMisses", "terrainLodConverting", "terrainLodEvictions", "terrainLodGpuBytes", "terrainLodHandoffOverlap", "terrainLodHandoffPreparing", "terrainLodHandoffReversals", "terrainLodHandoffsStarted", "terrainLodLevel0Resident", "terrainLodLevel1Resident", "terrainLodLevelTransitionReversals", "terrainLodLevelTransitions", "terrainLodLevelTransitionsStarted", "terrainLodMeshAdmissionDeferrals", "terrainLodMeshCompilationMsPerKCell", "terrainLodMeshCompilationSamples", "terrainLodMeshCompletedBytes", "terrainLodMeshCoverageCompleted", "terrainLodMeshCoverageQueued", "terrainLodMeshOversizedUploads", "terrainLodMeshOwned", "terrainLodMeshPredictedBytes", "terrainLodMeshPredictedMs", "terrainLodMeshRefinementCompleted", "terrainLodMeshRefinementQueued", "terrainLodMeshResultBytesPerKCell", "terrainLodMeshUploadBaseMs", "terrainLodMeshUploadDeferrals", "terrainLodMeshUploadMsPerMiB", "terrainLodMeshUploadSamples", "terrainLodPending", "terrainLodPresented", "terrainLodRejected", "terrainLodRemoteBytes", "terrainLodRemoteDeferred", "terrainLodRemoteMissing", "terrainLodRemotePending", "terrainLodRemoteRequests", "terrainLodRemoteTiles", "terrainLodResident", "terrainLodResourceGeneration", "terrainLodResourceReloads", "terrainLodResourceReusedColumns", "terrainLodResourceReusedGpuBytes", "terrainLodSolidCpuMs", "terrainLodSpatialComplete", "terrainLodSpatialCpuTiles", "terrainLodSpatialCurrentTiles", "terrainLodSpatialGpuResident", "terrainLodSpatialMeshPending", "terrainLodSpatialMeshQueued", "terrainLodSpatialMeshReady", "terrainLodSpatialMeshRunning", "terrainLodSpatialMissingGroups", "terrainLodSpatialParentFallbacks", "terrainLodSpatialSeamDesired", "terrainLodSpatialSeamGpuResident", "terrainLodSpatialSeamQueued", "terrainLodSpatialSeamReady", "terrainLodSpatialSeamRunning", "terrainLodSpatialSelected", "terrainLodStaleResults", "terrainLodTranslucentCpuMs", "terrainLodTranslucentPresented", "terrainLodUploads", "terrainPipelineBinds", "terrainSubmissionBatches", "terrainSubmitCpuMs", "terrainTextureBinds", "terrainUniformArenaCapacity", "terrainUniformArenaGrowths", "terrainUniformEntries", "translucentDrawsLastFrame", "visibilityBuildCancellations", "visibilityBuildInFlight", "visibilityBuilds", "visibilityCandidates", "visibilityReuseFrames", "visibilityStaleResults", "visibilitySynchronousFrames", "visibilityWorkerCandidates", "worldId", "worldLoaded"];

    private static readonly string[] s_additionalClientStateMembers =
        ["terrainLodSpatialSubmissionReady", "terrainLodSpatialAuthoritativeTiles",
            "terrainLodSpatialHighestAuthoritativeLevel", "terrainLodSpatialHighestGpuLevel",
            "terrainLodSpatialSolidPages", "terrainLodSpatialTranslucentPages",
            "terrainLodSpatialGpuBytes",
            "terrainCoveragePendingSeams",
            "terrainLodRemoteCoverageRequired", "terrainLodRemoteCoverageAvailable",
            "terrainLodRemoteCoverageInFlight", "terrainLodRemoteCoveragePending",
            "terrainLodRemoteCoverageMissing", "terrainLodRemoteCoverageDeferred",
            "terrainLodRemoteCoverageComplete",
            "terrainLodCoarseSourceUnavailable", "terrainLodCoarseBuilding",
            "terrainLodCoarseTransportPending", "terrainLodCoarseGpuPending",
            "terrainLodCoarseReady", "terrainLodCoarseAwaitingRequest",
            "terrainLodCoarseFrontierUnknown", "terrainLodCoarseComplete",
            "terrainLodCoarseRetainingPrevious", "terrainLodColdCoverMs",
            "terrainLodFirstCompleteHorizonMs", "terrainLodRefinementMs",
            "terrainLodIdentityReady", "terrainLodIdentityMismatches",
            "terrainLodIdentityRejectedMessages"];

    private static readonly string[] s_testMembers = ["breakBlock", "countEntities", "creative", "dumpProfiler", "dumpTerrain", "entityBaselineEnvironment", "fail", "flyPath", "isMeshCurrent", "meshDeadlineMissCount", "pass", "prepareTerrainLodFixture", "screenshot", "setBlock", "setFlying", "setLook", "setMovement", "summon", "teleport", "terrainLodFixtureMetric"];
    private static readonly string[] s_worldMembers = ["list", "load"];
    private static readonly string[] s_worldGenerationMembers = ["available", "get", "list", "start"];
    private static readonly string[] s_uiMembers = ["hud", "querySelector", "root", "screen"];

    private static readonly string[] s_configMembers =
    [
        "advancedItemTooltips", "alternateBlocks", "anisotropicLevel", "bobView", "cameraMode", "captureMouse",
        "chatScale", "chatWidth", "chunkFade", "cloudsQuality", "controllerSensitivity",
        "controllerType", "difficulty", "entityImpostorDistance", "fogDistance", "fov", "fpsLimit", "gamma", "guiScale", "invertYMouse",
        "language", "lastServer", "menuMusic", "mouseSensitivity", "msaaLevel", "music", "options", "pauseOnFocusLoss", "showCoordinates",
        "presentationQuality", "simulationDistance", "skin", "softClouds", "sound", "terrainHorizonDistance", "terrainLodDropoffDistance", "uiCursors", "useMipmaps", "viewDistance", "vsync"
    ];

    private static readonly string[] s_nodeMembers =
        ["child", "childCount", "click", "enabled", "hitTestVisible", "id", "parent", "text", "type", "visible"];

    private readonly HashSet<string> _sessionGlobals = new(StringComparer.Ordinal);

    public CompletionEdit Complete(string source, int cursor)
    {
        cursor = Math.Clamp(cursor, 0, source.Length);
        var start = cursor;
        while (start > 0 && IsIdentifierCharacter(source[start - 1]))
            start--;

        var prefix = source[start..cursor];
        var memberAccess = start > 0 && source[start - 1] == '.';
        var pool = memberAccess
            ? MembersForReceiver(source, start - 1)
            : s_globals.Concat(_sessionGlobals);

        var matches = pool
            .Where(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 0)
            return new CompletionEdit(start, cursor - start, prefix, matches);

        var replacement = LongestCommonPrefix(matches);
        return new CompletionEdit(start, cursor - start, replacement, matches);
    }

    public void ObserveSuccessfulSubmission(string source)
    {
        foreach (Match match in GlobalAssignmentRegex().Matches(source))
            _sessionGlobals.Add(match.Groups[1].Value);

        foreach (Match match in FunctionDeclarationRegex().Matches(source))
            _sessionGlobals.Add(match.Groups[1].Value);
    }

    private static bool IsIdentifierCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value == '_';

    private static IEnumerable<string> MembersForReceiver(string source, int dot)
    {
        var end = dot;
        var start = end;
        while (start > 0 && (IsIdentifierCharacter(source[start - 1]) || source[start - 1] == '.'))
            start--;
        var receiver = source[start..end];
        if (receiver == "OMNI") return s_omniMembers;
        if (receiver.EndsWith("OMNI.client", StringComparison.Ordinal)) return s_clientMembers;
        if (receiver.EndsWith("OMNI.client.state", StringComparison.Ordinal))
            return s_clientStateMembers.Concat(s_additionalClientStateMembers)
                .Concat(s_entityLodMembers);
        if (receiver.EndsWith("OMNI.test", StringComparison.Ordinal)) return s_testMembers;
        if (receiver.EndsWith("OMNI.client.worlds", StringComparison.Ordinal)) return s_worldMembers;
        if (receiver.EndsWith("OMNI.worldgen", StringComparison.Ordinal)) return s_worldGenerationMembers;
        if (receiver.EndsWith("OMNI.ui", StringComparison.Ordinal)) return s_uiMembers;
        if (receiver.EndsWith("OMNI.config", StringComparison.Ordinal)) return s_configMembers;
        return s_nodeMembers;
    }

    private static string LongestCommonPrefix(string[] values)
    {
        var first = values[0];
        var length = first.Length;
        foreach (var value in values.AsSpan(1))
        {
            length = Math.Min(length, value.Length);
            var i = 0;
            while (i < length && first[i] == value[i]) i++;
            length = i;
        }

        return first[..length];
    }

    [GeneratedRegex(@"(?m)(?:^|[;\n])\s*(?!local\b)([A-Za-z_][A-Za-z0-9_]*)\s*=")]
    private static partial Regex GlobalAssignmentRegex();

    [GeneratedRegex(@"(?m)(?:^|[;\n])\s*function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex FunctionDeclarationRegex();
}

internal readonly record struct CompletionEdit(int Start, int Length, string Replacement, IReadOnlyList<string> Matches);
