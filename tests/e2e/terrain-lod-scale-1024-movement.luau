-- Opt-in Phase 2B lifecycle gate. Every wait samples correctness and resource bounds; endpoint
-- dumps alone cannot detect a displayed partition disappearing during a replacement.
assert(OMNI.test.configureTerrainLodScaleProfile(1024))
OMNI.config.viewDistance = 0
OMNI.config.fogDistance = 1
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
local state = OMNI.client.state
local stage = "startup"
local samples = 0
local peakGpuBytes = 0
local peakBodyAge = 0
local peakSeamAge = 0

local function check(condition, reason)
    if not condition then
        print("scale-movement failure source=" .. tostring(state.terrainLodRemoteCoverageAvailable) ..
            "/" .. tostring(state.terrainLodRemoteCoverageRequired) ..
            " bodies=" .. tostring(state.terrainLodCoarseReady) ..
            " holes=" .. tostring(state.terrainCoverageHoles) ..
            " overlaps=" .. tostring(state.terrainCoverageOverlaps) ..
            " missingSeams=" .. tostring(state.terrainCoverageMissingSeams) ..
            " unexpectedSeams=" .. tostring(state.terrainCoverageUnexpectedSeams) ..
            " failureAt=" .. tostring(state.terrainCoverageFailureX) .. "," ..
            tostring(state.terrainCoverageFailureZ))
        OMNI.test.dumpTerrain("scale-movement-failed-" .. stage)
        OMNI.test.dumpProfiler("scale-movement-failed-" .. stage)
        error(stage .. ": " .. reason, 2)
    end
end

local function observe(requirePresentation)
    samples += 1
    peakGpuBytes = math.max(peakGpuBytes, state.terrainLodSpatialGpuBytes)
    peakBodyAge = math.max(peakBodyAge, state.terrainLodSpatialMeshOldestQueuedMs)
    peakSeamAge = math.max(peakSeamAge, state.terrainLodSpatialSeamOldestQueuedMs)
    check(state.webGpuErrorCount == 0, "WebGPU validation failed")
    check(state.meshBuildFailureCount == 0, "exact mesh build failed")
    check(state.terrainLodIdentityMismatches == 0 and
        state.terrainLodIdentityRejectedMessages == 0, "catalog negotiation failed")
    check(state.terrainLodSpatialSelected <= 1024, "selected-node budget exceeded")
    check(state.terrainLodSpatialGpuResident <= 2304, "GPU residency budget exceeded")
    check(state.terrainLodSpatialCpuTiles <= 4096, "CPU hierarchy budget exceeded")
    check(state.terrainLodSpatialGpuBytes <= 268435456, "GPU byte ceiling exceeded")
    check(state.terrainLodSpatialSolidPages <= 2048 and
        state.terrainLodSpatialTranslucentPages <= 2048, "draw-page budget exceeded")
    check(state.terrainLodSpatialMeshQueued <= 32 and
        state.terrainLodSpatialSeamQueued <= 64, "compilation queue budget exceeded")
    check(state.terrainLodSpatialMeshOldestQueuedMs < 30000 and
        state.terrainLodSpatialSeamOldestQueuedMs < 30000, "compilation starved")
    check(state.terrainLodSpatialMeshOverBudget == 0 and
        state.terrainLodSpatialSeamOverBudget == 0, "candidate exceeded byte budget")
    if requirePresentation then
        check(state.terrainLodSpatialSubmissionReady == 1, "published snapshot disappeared")
        check(state.terrainCoverageExpected > 0, "coverage oracle has no sample")
        check(state.terrainCoverageHoles == 0 and state.terrainCoverageOverlaps == 0,
            "coverage holes=" .. tostring(state.terrainCoverageHoles) ..
            " overlaps=" .. tostring(state.terrainCoverageOverlaps))
        check(state.terrainCoverageMissingSeams == 0 and
            state.terrainCoverageUnexpectedSeams == 0, "published seam contract failed")
    end
end

local function waitFor(predicate, seconds, requirePresentation)
    local ok, failure = pcall(function()
        OMNI.waitUntil(function()
            observe(requirePresentation)
            if samples % 100 == 0 then
                print("scale-movement waiting stage=" .. stage ..
                    " source=" .. tostring(state.terrainLodRemoteCoverageAvailable) ..
                    "/" .. tostring(state.terrainLodRemoteCoverageRequired) ..
                    " bodies=" .. tostring(state.terrainLodCoarseReady) ..
                    " missing=" .. tostring(state.terrainLodRemoteCoverageMissing) ..
                    " holes=" .. tostring(state.terrainCoverageHoles) ..
                    " overlaps=" .. tostring(state.terrainCoverageOverlaps) ..
                    " seams=" .. tostring(state.terrainCoverageMissingSeams) ..
                    "/" .. tostring(state.terrainCoverageUnexpectedSeams) ..
                    " gpuBytes=" .. tostring(state.terrainLodSpatialGpuBytes))
                OMNI.test.dumpTerrain("scale-movement-progress")
            end
            return predicate()
        end, seconds, 0.1)
    end)
    check(ok, tostring(failure))
end

local function complete()
    return state.playerReady and state.terrainLodRemoteCoverageComplete == 1 and
        state.terrainLodCoarseComplete == 1 and state.terrainLodCoarseFrontierUnknown == 0 and
        state.terrainLodSpatialSubmissionReady == 1 and state.terrainCoverageReady == 1
end

local function checkpoint(name)
    stage = name
    print("scale-movement stage=" .. stage .. " samples=" .. tostring(samples) ..
        " generation=" .. tostring(state.terrainLodConvergenceGeneration) ..
        " gpuBytes=" .. tostring(state.terrainLodSpatialGpuBytes) ..
        " bodyCancelled=" .. tostring(state.terrainLodSpatialMeshCancelled) ..
        " seamCancelled=" .. tostring(state.terrainLodSpatialSeamCancelled))
    OMNI.test.dumpTerrain("scale-movement-" .. stage)
    OMNI.test.dumpProfiler("scale-movement-" .. stage)
end

assert(OMNI.client.worlds.load("e2e-smoke"))
waitFor(function() return state.worldLoaded and state.playerReady end, 75, false)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(0, 192, 320)
waitFor(function()
    return state.playerReady and math.abs(state.playerX) < 1 and
        math.abs(state.playerZ - 320) < 1 and state.playerY > 180
end, 45, false)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
-- At Y=192 the shallow fixed-camera view contains no columns in the oracle's bounded near
-- footprint. Look down so readiness proves actual audited terrain rather than an empty sample.
OMNI.test.setLook(180, 70)
waitFor(complete, 180, false)
check(state.terrainLodSpatialHighestAuthoritativeLevel >= 8, "L8 was never presented")
check(OMNI.test.terrainLodFixtureMetric("cacheReadHits") > 0, "persistent cache was not reopened")
observe(true)
checkpoint("cold-ready")

stage = "rotation"
for _, yaw in ipairs({0, 90, 180, 270}) do
    OMNI.test.setLook(yaw, 70)
    for _ = 1, 5 do OMNI.wait(0.1); observe(true) end
end

stage = "flight"
local generation = state.terrainLodConvergenceGeneration
OMNI.test.setLook(0, 55)
OMNI.test.flyPath(0, 192, 320, 0, 192, 480, 20)
for chunk = 1, 10 do
    stage = "flight-" .. tostring(chunk)
    waitFor(function() return state.playerZ >= 320 + chunk * 16 - 0.5 end, 30, true)
    check(state.playerY > 180, "player fell during flight")
end
check(state.terrainLodConvergenceGeneration > generation, "flight did not recenter coverage")
waitFor(complete, 120, true)
checkpoint("flight-ready")

stage = "teleport"
OMNI.test.teleport(160, 192, 640)
waitFor(function()
    return state.playerReady and math.abs(state.playerX - 160) < 1 and
        math.abs(state.playerZ - 640) < 1
end, 45, true)
OMNI.test.setFlying(true)
waitFor(complete, 120, true)
checkpoint("teleport-ready")

stage = "edits"
for edit = 1, 24 do
    -- Stay below the world ceiling: the aerial camera itself is above editable terrain.
    local block = edit % 2 == 0 and "omniblock:stone" or "omniblock:air"
    OMNI.test.setBlock(block, 175, 100, 640)
    waitFor(function() return OMNI.test.hasBlock(block, 175, 100, 640) end, 10, true)
end
OMNI.test.setBlock("omniblock:stone", 175, 100, 640)
OMNI.wait(0.5)
waitFor(function()
    return OMNI.test.hasBlock("omniblock:stone", 175, 100, 640) and
        OMNI.test.isMeshCurrent(175, 100, 640)
end, 30, true)
checkpoint("edits-ready")

stage = "recenter"
OMNI.test.teleport(0, 192, 320)
waitFor(function()
    return state.playerReady and math.abs(state.playerX) < 1 and
        math.abs(state.playerZ - 320) < 1
end, 45, true)
OMNI.test.setFlying(true)
waitFor(complete, 120, true)
checkpoint("recenter-ready")

-- Leave while a new leading edge has work in flight, then allow callbacks to settle at the menu.
stage = "disconnect-pending"
OMNI.test.flyPath(0, 192, 320, 0, 192, 480, 4)
waitFor(function()
    return state.terrainLodSpatialMeshPending + state.terrainLodSpatialMeshQueued +
        state.terrainLodSpatialMeshRunning + state.terrainLodSpatialSeamQueued +
        state.terrainLodSpatialSeamRunning + state.terrainLodRemoteCoverageInFlight > 0
end, 20, true)
checkpoint("disconnect-pending")
check(OMNI.test.disconnect(), "disconnect was rejected")
waitFor(function() return not state.worldLoaded and OMNI.ui.screen == "MainMenuScreen" end, 30, false)
for _ = 1, 20 do
    OMNI.wait(0.1)
    check(not state.worldLoaded and not state.playerReady, "world returned after disconnect")
    check(state.webGpuErrorCount == 0, "late GPU failure after disconnect")
    check(state.terrainLodSpatialGpuResident == 0 and state.terrainLodSpatialGpuBytes == 0 and
        state.terrainLodSpatialMeshQueued == 0 and state.terrainLodSpatialSeamQueued == 0,
        "old terrain resources survived disconnect")
end
print("scale-movement passed samples=" .. tostring(samples) ..
    " peakGpuBytes=" .. tostring(peakGpuBytes) ..
    " peakBodyAgeMs=" .. tostring(peakBodyAge) .. " peakSeamAgeMs=" .. tostring(peakSeamAge))
OMNI.test.pass()
