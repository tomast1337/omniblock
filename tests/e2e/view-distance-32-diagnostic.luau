OMNI.config.viewDistance = 1.0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")

OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

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
        "/" .. tostring(OMNI.client.state.translucentDrawsLastFrame))
end
OMNI.test.pass()
