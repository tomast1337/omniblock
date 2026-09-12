OMNI.config.viewDistance = 1.0
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")

OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.setLook(0, 0)

for sample = 1, 6 do
    OMNI.wait(5)
    local frameTimeMs = OMNI.client.state.frameTimeMs
    local meshPending = OMNI.client.state.meshPending

    assert(frameTimeMs > 0, "frame-time metric did not advance")
    assert(meshPending < 8192, "distance-32 mesh backlog is unbounded: " .. tostring(meshPending))

    print("distance32 frame=" .. tostring(frameTimeMs) ..
        "ms pending=" .. tostring(meshPending) ..
        " requestToGpu=" .. tostring(OMNI.client.state.meshRequestToGpuMs) .. "ms" ..
        " uploads=" .. tostring(OMNI.client.state.geometryUploadsLastFrame) ..
        "/" .. tostring(OMNI.client.state.lightUploadsLastFrame) ..
        " draws=" .. tostring(OMNI.client.state.solidDrawsLastFrame) ..
        "/" .. tostring(OMNI.client.state.translucentDrawsLastFrame) ..
        " resident=" .. tostring(OMNI.client.state.residentMeshCount) ..
        " candidates=" .. tostring(OMNI.client.state.visibilityCandidates) ..
        " frustumTests=" .. tostring(OMNI.client.state.frustumTests) ..
        " portalVisited=" .. tostring(OMNI.client.state.portalVisited) ..
        " safetyRescued=" .. tostring(OMNI.client.state.safetyRescued) ..
        " layers=" .. tostring(OMNI.client.state.presentedSolidLayerCount) ..
        "/" .. tostring(OMNI.client.state.presentedTranslucentLayerCount) ..
        " emptySubmitted=" .. tostring(OMNI.client.state.emptyLayersSubmitted) ..
        " terrainDraws=" .. tostring(OMNI.client.state.terrainDrawCalls) ..
        " uniforms=" .. tostring(OMNI.client.state.terrainUniformEntries) ..
        " findVisible=" .. tostring(OMNI.client.state.findVisibleMs) .. "ms" ..
        " submit=" .. tostring(OMNI.client.state.terrainSubmitCpuMs) .. "ms")
end
OMNI.test.dumpTerrain("visibility-v32-30s")
OMNI.test.pass()
