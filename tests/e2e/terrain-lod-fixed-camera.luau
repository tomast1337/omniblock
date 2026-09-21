-- Phase 5 fixed-camera scale gate. The runner's preparation process has already written the exact
-- adaptive 64-chunk radial partition. This fresh process must reopen and transport those records
-- before the timed client compilation, upload, and presentation sample begins.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 1
OMNI.config.fogDistance = 1
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.test.teleport(0, 192, 320)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady and OMNI.client.state.playerY > 180 and
        OMNI.client.state.playerZ > 300
end, 45, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(180, 25)

OMNI.waitUntil(function()
    return OMNI.client.state.terrainLodRemoteCoverageRequired > 0 and
        OMNI.client.state.terrainLodRemoteCoverageComplete == 1
end, 120, 0.05)
assert(OMNI.test.terrainLodFixtureMetric("cacheReadHits") > 0,
    "measured process did not reopen persistent terrain LOD records")

local spatialReady = false
for _ = 1, 2400 do
    spatialReady = OMNI.client.state.terrainLodSpatialSubmissionReady == 1 and
        OMNI.client.state.terrainLodCoarseComplete == 1 and
        OMNI.client.state.terrainLodCoarseFrontierUnknown == 0 and
        OMNI.client.state.terrainLodSpatialGpuResident > 0 and
        OMNI.client.state.terrainLodSpatialSolidPages > 0 and
        OMNI.client.state.terrainLodSpatialGpuBytes > 0
    if spatialReady then break end
    OMNI.wait(0.05)
end
assert(spatialReady,
    "spatial terrain did not become presentable: resident=" ..
    tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " pending=" .. tostring(OMNI.client.state.terrainLodSpatialMeshPending) ..
    " queued=" .. tostring(OMNI.client.state.terrainLodSpatialMeshQueued) ..
    " running=" .. tostring(OMNI.client.state.terrainLodSpatialMeshRunning) ..
    " seams=" .. tostring(OMNI.client.state.terrainLodSpatialSeamGpuResident) ..
    "/" .. tostring(OMNI.client.state.terrainLodSpatialSeamDesired))
OMNI.wait(2)

local fixedX = OMNI.client.state.playerX
local fixedY = OMNI.client.state.playerY
local fixedZ = OMNI.client.state.playerZ
local samples = {
    frame = {}, lodCpu = {}, nearSubmitCpu = {}, lodDraws = {}, nearDraws = {},
    resident = {}, presented = {}, gpuBytes = {}, boundaryBytes = {}, cacheBytes = {},
    workingSetBytes = {}
}

for _ = 1, 120 do
    OMNI.wait(0.05)
    table.insert(samples.frame, OMNI.client.state.frameTimeMs)
    table.insert(samples.lodCpu,
        OMNI.client.state.terrainLodSolidCpuMs + OMNI.client.state.terrainLodTranslucentCpuMs)
    table.insert(samples.nearSubmitCpu, OMNI.client.state.terrainSubmitCpuMs)
    table.insert(samples.lodDraws,
        OMNI.client.state.terrainLodSpatialSolidPages +
        OMNI.client.state.terrainLodSpatialTranslucentPages)
    table.insert(samples.nearDraws, OMNI.client.state.terrainDrawCalls)
    table.insert(samples.resident, OMNI.client.state.terrainLodSpatialGpuResident)
    table.insert(samples.presented, OMNI.client.state.terrainLodSpatialSolidPages)
    table.insert(samples.gpuBytes, OMNI.client.state.terrainLodSpatialGpuBytes)
    table.insert(samples.boundaryBytes, OMNI.client.state.terrainLodBoundaryBytes)
    table.insert(samples.cacheBytes, OMNI.client.state.terrainLodCacheBytes)
    table.insert(samples.workingSetBytes, OMNI.client.state.clientWorkingSetBytes)
end

local function distribution(values)
    table.sort(values)
    local total = 0
    for _, value in ipairs(values) do total += value end
    local function percentile(p)
        return values[math.max(1, math.ceil(#values * p))]
    end
    return { mean = total / #values, p50 = percentile(0.50), p95 = percentile(0.95) }
end

local summary = {}
for key, values in pairs(samples) do summary[key] = distribution(values) end
local requiredTiles = OMNI.client.state.terrainLodRemoteCoverageRequired
local availableTiles = OMNI.client.state.terrainLodRemoteCoverageAvailable
local tileCoverage = requiredTiles > 0 and availableTiles / requiredTiles or 0

assert(math.abs(OMNI.client.state.playerX - fixedX) < 0.01 and
    math.abs(OMNI.client.state.playerY - fixedY) < 0.01 and
    math.abs(OMNI.client.state.playerZ - fixedZ) < 0.01,
    "camera moved during the fixed-camera sample")
assert(summary.frame.mean > 0 and summary.lodCpu.mean > 0,
    "CPU timing counters did not advance")
assert(summary.gpuBytes.mean > 0 and summary.workingSetBytes.mean > 0,
    "memory counters did not advance")
assert(OMNI.client.state.webGpuErrorCount == 0, "WebGPU validation failed")
assert(OMNI.client.state.terrainLodRemoteCoverageComplete == 1 and
        availableTiles == requiredTiles,
    "64-chunk terrain coverage regressed during the fixed-camera sample")

print("terrain-lod-fixed-camera schema=2 targetChunks=64 spatialResident=" ..
    tostring(summary.resident.mean) ..
    " spatialPresented=" .. tostring(summary.presented.mean) ..
    " requiredTiles=" .. tostring(requiredTiles) ..
    " availableTiles=" .. tostring(availableTiles) ..
    " tileCoverage=" .. tostring(tileCoverage) ..
    " coverageInFlight=" .. tostring(OMNI.client.state.terrainLodRemoteCoverageInFlight) ..
    " coveragePending=" .. tostring(OMNI.client.state.terrainLodRemoteCoveragePending) ..
    " coverageMissing=" .. tostring(OMNI.client.state.terrainLodRemoteCoverageMissing) ..
    " coverageDeferred=" .. tostring(OMNI.client.state.terrainLodRemoteCoverageDeferred) ..
    " serverCacheReadHits=" .. tostring(OMNI.test.terrainLodFixtureMetric("cacheReadHits")) ..
    " frameMeanMs=" .. tostring(summary.frame.mean) ..
    " frameP50Ms=" .. tostring(summary.frame.p50) ..
    " frameP95Ms=" .. tostring(summary.frame.p95) ..
    " lodCpuMeanMs=" .. tostring(summary.lodCpu.mean) ..
    " lodCpuP95Ms=" .. tostring(summary.lodCpu.p95) ..
    " nearSubmitCpuMeanMs=" .. tostring(summary.nearSubmitCpu.mean) ..
    " nearSubmitCpuP95Ms=" .. tostring(summary.nearSubmitCpu.p95) ..
    " lodDrawsMean=" .. tostring(summary.lodDraws.mean) ..
    " nearDrawsMean=" .. tostring(summary.nearDraws.mean) ..
    " lodGpuBytes=" .. tostring(summary.gpuBytes.mean) ..
    " boundaryBytes=" .. tostring(summary.boundaryBytes.mean) ..
    " cacheBytes=" .. tostring(summary.cacheBytes.mean) ..
    " workingSetBytes=" .. tostring(summary.workingSetBytes.mean) ..
    " coverageComplete=" .. tostring(OMNI.client.state.terrainLodRemoteCoverageComplete))

OMNI.test.dumpTerrain("lod-fixed-camera-64")
OMNI.test.dumpProfiler("lod-fixed-camera-64")
OMNI.test.screenshot()
OMNI.test.pass()
