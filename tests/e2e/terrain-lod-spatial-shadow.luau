OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 1
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

assert(type(OMNI.client.state.terrainLodSpatialComplete) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSelected) == "number")
assert(type(OMNI.client.state.terrainLodSpatialGpuResident) == "number")
assert(type(OMNI.client.state.terrainLodSpatialCpuTiles) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMissingGroups) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshPending) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshQueued) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshRunning) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshReady) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSeamDesired) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSeamGpuResident) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSubmissionReady) == "number")
assert(type(OMNI.client.state.terrainLodSpatialAuthoritativeTiles) == "number")

local stagingReady = false
for _ = 1, 600 do
    stagingReady = OMNI.client.state.terrainLodSpatialCpuTiles > 0 and
        OMNI.client.state.terrainLodSpatialGpuResident > 0 and
        OMNI.client.state.terrainLodSpatialComplete == 1 and
        OMNI.client.state.terrainLodSpatialSelected > 0 and
        OMNI.client.state.terrainLodSpatialSeamDesired > 0 and
        OMNI.client.state.terrainLodSpatialSeamGpuResident > 0 and
        OMNI.client.state.terrainLodSpatialSubmissionReady == 1
    if stagingReady then break end
    OMNI.wait(0.05)
end
assert(stagingReady,
    "spatial presentation staging did not become ready: cpu=" ..
    tostring(OMNI.client.state.terrainLodSpatialCpuTiles) ..
    " gpu=" .. tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " complete=" .. tostring(OMNI.client.state.terrainLodSpatialComplete) ..
    " selected=" .. tostring(OMNI.client.state.terrainLodSpatialSelected) ..
    " seamDesired=" .. tostring(OMNI.client.state.terrainLodSpatialSeamDesired) ..
    " seamGpu=" .. tostring(OMNI.client.state.terrainLodSpatialSeamGpuResident) ..
    " submission=" .. tostring(OMNI.client.state.terrainLodSpatialSubmissionReady) ..
    " authoritative=" .. tostring(OMNI.client.state.terrainLodSpatialAuthoritativeTiles))

-- Move far enough that the retained roots around spawn lie wholly outside the exact-render guard
-- band, then look back at them. This makes the live spatial path authoritative without ever
-- allowing a coarse tile to overlap the near renderer.
local px = OMNI.client.state.playerX
local pz = OMNI.client.state.playerZ
OMNI.test.creative()
OMNI.test.setFlying(true)
OMNI.test.teleport(px, 192, pz + 320)
OMNI.test.setLook(180, 25)
OMNI.waitUntil(function()
    return OMNI.client.state.playerY > 180 and OMNI.client.state.playerZ > pz + 300
end, 10, 0.05)

local liveReady = false
for _ = 1, 600 do
    liveReady = OMNI.client.state.terrainLodSpatialSubmissionReady == 1 and
        OMNI.client.state.terrainLodSpatialAuthoritativeTiles > 0 and
        OMNI.client.state.terrainLodSpatialSolidPages > 0
    if liveReady then break end
    OMNI.wait(0.05)
end
assert(liveReady,
    "spatial roots did not become authoritative after moving away: submission=" ..
    tostring(OMNI.client.state.terrainLodSpatialSubmissionReady) ..
    " authoritative=" .. tostring(OMNI.client.state.terrainLodSpatialAuthoritativeTiles) ..
    " solidPages=" .. tostring(OMNI.client.state.terrainLodSpatialSolidPages) ..
    " translucentPages=" .. tostring(OMNI.client.state.terrainLodSpatialTranslucentPages))

assert(OMNI.client.state.webGpuErrorCount == 0,
    "WebGPU validation failed while staging spatial terrain")
print("spatial LOD live: cpu=" ..
    tostring(OMNI.client.state.terrainLodSpatialCpuTiles) ..
    " gpu=" .. tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " selected=" .. tostring(OMNI.client.state.terrainLodSpatialSelected) ..
    " seams=" .. tostring(OMNI.client.state.terrainLodSpatialSeamGpuResident) ..
    " live=" .. tostring(OMNI.client.state.terrainLodSpatialAuthoritativeTiles) ..
    " fallbacks=" .. tostring(OMNI.client.state.terrainLodSpatialParentFallbacks) ..
    " missing=" .. tostring(OMNI.client.state.terrainLodSpatialMissingGroups))
OMNI.test.pass()
