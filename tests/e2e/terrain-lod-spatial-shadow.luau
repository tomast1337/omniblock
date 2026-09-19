OMNI.config.viewDistance = 1.0
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

OMNI.waitUntil(function()
    return OMNI.client.state.terrainLodSpatialCpuTiles > 0 and
        OMNI.client.state.terrainLodSpatialGpuResident > 0 and
        OMNI.client.state.terrainLodSpatialComplete == 1 and
        OMNI.client.state.terrainLodSpatialSelected > 0
end, 30, 0.05)

assert(OMNI.client.state.webGpuErrorCount == 0,
    "WebGPU validation failed while staging spatial terrain")
print("spatial LOD shadow: cpu=" ..
    tostring(OMNI.client.state.terrainLodSpatialCpuTiles) ..
    " gpu=" .. tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " selected=" .. tostring(OMNI.client.state.terrainLodSpatialSelected) ..
    " fallbacks=" .. tostring(OMNI.client.state.terrainLodSpatialParentFallbacks) ..
    " missing=" .. tostring(OMNI.client.state.terrainLodSpatialMissingGroups))
OMNI.test.pass()
