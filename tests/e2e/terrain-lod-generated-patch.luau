-- Bounded real-source gate, NOT a complete 64/512/1024 horizon benchmark. Unknown terrain
-- outside the generated patch must remain unknown rather than receive synthetic flat tiles.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
local state = OMNI.client.state
local stage = "startup"
local samples = 0
local peakGpuBytes = 0
local peakBodyAgeMs = 0
local peakSeamAgeMs = 0

local function check(condition, reason)
    if condition then return end
    OMNI.test.dumpTerrain("generated-patch-failed-" .. stage)
    OMNI.test.dumpProfiler("generated-patch-failed-" .. stage)
    error(stage .. ": " .. reason, 2)
end

local function observe(requireCoverage)
    samples += 1
    peakGpuBytes = math.max(peakGpuBytes, state.terrainLodSpatialGpuBytes)
    peakBodyAgeMs = math.max(peakBodyAgeMs, state.terrainLodSpatialMeshOldestQueuedMs)
    peakSeamAgeMs = math.max(peakSeamAgeMs, state.terrainLodSpatialSeamOldestQueuedMs)
    check(state.webGpuErrorCount == 0, "WebGPU validation failed")
    check(state.meshBuildFailureCount == 0, "exact meshing failed")
    check(state.terrainLodIdentityMismatches == 0 and
        state.terrainLodIdentityRejectedMessages == 0, "cache identity mismatch")
    check(state.terrainLodSpatialGpuBytes <= 268435456, "GPU budget exceeded")
    check(state.terrainLodSpatialMeshQueued <= 32 and
        state.terrainLodSpatialSeamQueued <= 64, "compilation queue budget exceeded")
    check(state.terrainLodSpatialMeshOldestQueuedMs < 30000 and
        state.terrainLodSpatialSeamOldestQueuedMs < 30000, "compilation starved")
    if requireCoverage then
        check(state.terrainCoverageExpected > 0, "empty camera coverage sample")
        check(state.terrainCoverageHoles == 0 and state.terrainCoverageOverlaps == 0,
            "local presentation holes=" .. state.terrainCoverageHoles ..
            " overlaps=" .. state.terrainCoverageOverlaps)
        check(state.terrainCoverageMissingSeams == 0 and
            state.terrainCoverageUnexpectedSeams == 0, "published seam contract failed")
    end
end

local function waitFor(predicate, seconds, requireCoverage)
    local ok, failure = pcall(function()
        OMNI.waitUntil(function()
            observe(requireCoverage)
            return predicate()
        end, seconds, 0.1)
    end)
    check(ok, tostring(failure))
end

local function checkpoint(name)
    stage = name
    OMNI.test.dumpTerrain("generated-patch-" .. stage)
    OMNI.test.dumpProfiler("generated-patch-" .. stage)
    print("generated-patch stage=" .. stage .. " samples=" .. samples ..
        " coverage=" .. state.terrainCoverageCovered .. "/" .. state.terrainCoverageExpected)
end

local function localReady()
    -- This is the existing camera-footprint ownership oracle, not a claim that the missing
    -- outer horizon is covered. Keep its denominator nonzero to avoid a vacuous success.
    return state.playerReady and state.terrainCoverageReady == 1 and
        state.terrainCoverageExpected > 0 and state.terrainLodSpatialSubmissionReady == 1
end

assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

local prepared = nil
for _, job in OMNI.worldgen.list() do
    if job.id == "e2e_generated_lod_patch" then prepared = job end
end
assert(prepared ~= nil and prepared.status == "completed", "prepared job was not reopened")
assert(prepared.writtenChunks > 0, "preparation did not commit actual chunks")
assert(prepared.radiusChunks == 32 and prepared.totalTargets == 3209, "wrong generated-patch footprint")
assert(prepared.generatorProfile == "omniblock:default", "wrong prepared generator")
local tilesBefore = state.terrainLodRemoteTiles
OMNI.test.teleport(1024, 192, 1024)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 1024) < 1 and
        math.abs(state.playerZ - 1024) < 1 and state.playerY > 180
end, 60, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 70)
OMNI.waitUntil(function()
    return state.terrainLodRemoteTiles > tilesBefore and
        OMNI.test.terrainLodFixtureMetric("cacheReadHits") > 0 and
        state.terrainLodSpatialGpuResident > 0 and state.terrainLodSpatialSubmissionReady == 1
end, 120, 0.1)

stage = "initial-coverage"
waitFor(localReady, 60, false)
checkpoint("initial-ready")
for direction = 0, 7 do
    stage = "rotation-" .. direction
    OMNI.test.setLook(direction * 45, 70)
    for _ = 1, 10 do OMNI.wait(0.1); observe(true) end
    OMNI.test.dumpTerrain("generated-patch-rotation-" .. direction)
end

-- Stay inside the prepared disk patch, while crossing four exact/LOD ownership boundaries.
stage = "flight"
local generation = state.terrainLodConvergenceGeneration
OMNI.test.setLook(0, 70)
OMNI.test.flyPath(1024, 192, 1024, 1024, 192, 1088, 12)
for chunk = 1, 4 do
    stage = "flight-" .. chunk
    waitFor(function() return state.playerZ >= 1024 + chunk * 16 - 0.5 end, 20, true)
    check(state.playerY > 180, "player fell during flight")
end
check(state.terrainLodConvergenceGeneration > generation, "flight never recentered coverage")
waitFor(localReady, 60, false)
checkpoint("flight-ready")

stage = "teleport"
OMNI.test.teleport(992, 192, 1024)
waitFor(function()
    return state.playerReady and math.abs(state.playerX - 992) < 1 and
        math.abs(state.playerZ - 1024) < 1
end, 60, false)
OMNI.test.setFlying(true)
waitFor(localReady, 60, false)
checkpoint("teleport-ready")

stage = "edits"
for edit = 1, 8 do
    local block = edit % 2 == 1 and "omniblock:stone" or "omniblock:air"
    OMNI.test.setBlock(block, 1007, 100, 1024)
    waitFor(function() return OMNI.test.hasBlock(block, 1007, 100, 1024) end, 10, true)
end
waitFor(function() return OMNI.test.isMeshCurrent(1007, 100, 1024) end, 30, true)
checkpoint("edits-ready")

stage = "return"
OMNI.test.teleport(1024, 192, 1024)
waitFor(function()
    return state.playerReady and math.abs(state.playerX - 1024) < 1 and
        math.abs(state.playerZ - 1024) < 1
end, 60, false)
OMNI.test.setFlying(true)
waitFor(localReady, 60, false)
for _ = 1, 20 do OMNI.wait(0.1); observe(true) end
checkpoint("return-ready")
OMNI.test.dumpProfiler("generated-patch")
print("real generated patch: remoteTiles=" .. state.terrainLodRemoteTiles ..
    " cacheHits=" .. OMNI.test.terrainLodFixtureMetric("cacheReadHits") ..
    " resident=" .. state.terrainLodSpatialGpuResident ..
    " missing=" .. state.terrainLodRemoteCoverageMissing ..
    " samples=" .. samples .. " peakGpuBytes=" .. peakGpuBytes ..
    " peakBodyAgeMs=" .. peakBodyAgeMs .. " peakSeamAgeMs=" .. peakSeamAgeMs)
OMNI.test.pass()
