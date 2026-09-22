-- First dormant-level release gate. The runner prepared the exact radial partition in a separate
-- process; this launch must reopen it and exercise real L7 negotiation and presentation.
local horizon = 512
local expectedLevel = 7
assert(OMNI.test.configureTerrainLodScaleProfile(horizon), "could not select scale profile")
OMNI.config.viewDistance = 0
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
    return OMNI.client.state.playerReady and OMNI.client.state.playerY > 180
end, 45, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(180, 25)

local remoteWaitSteps = 0
for step = 1, 6000 do
    remoteWaitSteps = step
    if OMNI.client.state.terrainLodRemoteCoverageRequired > 0 and
        OMNI.client.state.terrainLodRemoteCoverageComplete == 1 then break end
    OMNI.wait(0.05)
end
if OMNI.client.state.terrainLodRemoteCoverageComplete ~= 1 then
    OMNI.test.dumpTerrain("lod-scale-512-transport-timeout")
    OMNI.test.dumpProfiler("lod-scale-512-transport-timeout")
end
assert(OMNI.client.state.terrainLodRemoteCoverageComplete == 1,
    "remote coverage did not converge")
assert(OMNI.test.terrainLodFixtureMetric("cacheReadHits") > 0,
    "measured process did not reopen persistent records")
local presentationWaitSteps = 0
local presentationReady = false
for step = 1, 6000 do
    presentationWaitSteps = step
    presentationReady = OMNI.client.state.terrainLodSpatialSubmissionReady == 1 and
        OMNI.client.state.terrainLodCoarseComplete == 1 and
        OMNI.client.state.terrainLodCoarseFrontierUnknown == 0 and
        OMNI.client.state.terrainLodSpatialHighestAuthoritativeLevel >= expectedLevel and
        OMNI.client.state.terrainLodSpatialGpuResident > 0
    if presentationReady then break end
    OMNI.wait(0.05)
end
if not presentationReady then
    OMNI.test.dumpTerrain("lod-scale-512-presentation-timeout")
    OMNI.test.dumpProfiler("lod-scale-512-presentation-timeout")
end
assert(presentationReady, "spatial presentation did not converge")
OMNI.wait(2)

local required = OMNI.client.state.terrainLodRemoteCoverageRequired
local available = OMNI.client.state.terrainLodRemoteCoverageAvailable
assert(required > 0 and required <= 512 and available == required, "coverage is incomplete or unbounded")
assert(OMNI.client.state.terrainLodRemoteCoverageInFlight == 0 and
        OMNI.client.state.terrainLodRemoteCoveragePending == 0 and
        OMNI.client.state.terrainLodRemoteCoverageMissing == 0 and
        OMNI.client.state.terrainLodRemoteCoverageDeferred == 0,
    "coverage did not reach a steady state")
assert(OMNI.client.state.terrainLodSpatialSelected <= 1024, "selected-node budget exceeded")
assert(OMNI.client.state.terrainLodSpatialSolidPages <= 2048 and
        OMNI.client.state.terrainLodSpatialTranslucentPages <= 2048, "draw-page budget exceeded")
assert(OMNI.client.state.terrainLodSpatialGpuResident <= 2304, "GPU presentation budget exceeded")
assert(OMNI.client.state.terrainLodSpatialCpuTiles <= 4096, "CPU hierarchy budget exceeded")
assert(OMNI.client.state.terrainLodSpatialGpuBytes <= 268435456, "GPU byte ceiling exceeded")
assert(OMNI.client.state.terrainLodSpatialMeshQueued <= 32 and
        OMNI.client.state.terrainLodSpatialSeamQueued <= 64, "compilation queue budget exceeded")
assert(OMNI.client.state.terrainLodSpatialMeshOldestQueuedMs < 30000 and
        OMNI.client.state.terrainLodSpatialSeamOldestQueuedMs < 30000, "compilation queue starved")
assert(OMNI.client.state.terrainLodSpatialMeshOverBudget == 0 and
        OMNI.client.state.terrainLodSpatialSeamOverBudget == 0, "compiled result exceeded admission budget")
assert(OMNI.client.state.terrainLodIdentityMismatches == 0 and
        OMNI.client.state.terrainLodIdentityRejectedMessages == 0, "LOD negotiation failed")
assert(OMNI.client.state.webGpuErrorCount == 0, "WebGPU validation failed")

print("terrain-lod-scale schema=1 targetChunks=" .. tostring(horizon) ..
    " required=" .. tostring(required) .. " available=" .. tostring(available) ..
    " selected=" .. tostring(OMNI.client.state.terrainLodSpatialSelected) ..
    " resident=" .. tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " gpuBytes=" .. tostring(OMNI.client.state.terrainLodSpatialGpuBytes) ..
    " workingSetBytes=" .. tostring(OMNI.client.state.clientWorkingSetBytes) ..
    " transportWaitMs=" .. tostring(remoteWaitSteps * 50) ..
    " presentationWaitMs=" .. tostring(presentationWaitSteps * 50) ..
    " firstRequestMs=" .. tostring(OMNI.client.state.terrainLodFirstRequestMs) ..
    " firstSourceMs=" .. tostring(OMNI.client.state.terrainLodFirstSourceTileMs) ..
    " sourceCompleteMs=" .. tostring(OMNI.client.state.terrainLodSourceCompleteMs) ..
    " firstBodyMs=" .. tostring(OMNI.client.state.terrainLodFirstBodyUploadMs) ..
    " bodiesCompleteMs=" .. tostring(OMNI.client.state.terrainLodBodiesCompleteMs) ..
    " firstSeamMs=" .. tostring(OMNI.client.state.terrainLodFirstSeamUploadMs) ..
    " seamsCompleteMs=" .. tostring(OMNI.client.state.terrainLodSeamsCompleteMs) ..
    " publicationMs=" .. tostring(OMNI.client.state.terrainLodPublicationMs) ..
    " networkReceived=" .. tostring(OMNI.client.state.terrainLodNetworkTilesReceived) ..
    " networkAdmitted=" .. tostring(OMNI.client.state.terrainLodNetworkTilesAdmitted) ..
    " clientTileQueuePeak=" .. tostring(OMNI.client.state.terrainLodNetworkTileQueuePeak) ..
    " transportQueuePeak=" .. tostring(OMNI.client.state.terrainLodTransportQueuePeak) ..
    " serverCacheBytes=" .. tostring(OMNI.test.terrainLodFixtureMetric("cacheBytes")) ..
    " serverCacheReadHits=" .. tostring(OMNI.test.terrainLodFixtureMetric("cacheReadHits")) ..
    " serverReadQueued=" .. tostring(OMNI.test.terrainLodFixtureMetric("readQueued")) ..
    " serverReadPending=" .. tostring(OMNI.test.terrainLodFixtureMetric("readPending")) ..
    " serverEncodeQueued=" .. tostring(OMNI.test.terrainLodFixtureMetric("encodeQueued")) ..
    " serverEncodePending=" .. tostring(OMNI.test.terrainLodFixtureMetric("encodePending")) ..
    " serverWirePayloads=" .. tostring(OMNI.test.terrainLodFixtureMetric("wirePayloads")) ..
    " meshCompleted=" .. tostring(OMNI.client.state.terrainLodSpatialMeshCompleted) ..
    " meshWorkerMs=" .. tostring(OMNI.client.state.terrainLodSpatialMeshCompilationTotalMs) ..
    " meshMaxMs=" .. tostring(OMNI.client.state.terrainLodSpatialMeshCompilationMaxMs) ..
    " meshPeakQueued=" .. tostring(OMNI.client.state.terrainLodSpatialMeshPeakQueued) ..
    " meshPeakRunning=" .. tostring(OMNI.client.state.terrainLodSpatialMeshPeakRunning) ..
    " meshPeakCompleted=" .. tostring(OMNI.client.state.terrainLodSpatialMeshPeakCompleted) ..
    " meshCancelled=" .. tostring(OMNI.client.state.terrainLodSpatialMeshCancelled) ..
    " seamCancelled=" .. tostring(OMNI.client.state.terrainLodSpatialSeamCancelled))
OMNI.test.dumpTerrain("lod-scale-512")
OMNI.test.dumpProfiler("lod-scale-512")
OMNI.test.pass()
