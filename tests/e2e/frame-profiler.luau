-- Opt-in steady-camera frame diagnosis at the maximum near-terrain distance. The two orientations
-- make frustum-sensitive work obvious without screenshots or input focus.
OMNI.config.viewDistance = 1.0
OMNI.config.terrainHorizonDistance = 0
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
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(0, 0)
OMNI.wait(8)

print("frame-profiler forward frameMs=" .. tostring(OMNI.client.state.frameTimeMs) ..
    " resident=" .. tostring(OMNI.client.state.residentMeshCount) ..
    " candidates=" .. tostring(OMNI.client.state.visibilityCandidates) ..
    " presented=" .. tostring(OMNI.client.state.presentedMeshCount) ..
    " findVisibleMs=" .. tostring(OMNI.client.state.findVisibleMs) ..
    " submitMs=" .. tostring(OMNI.client.state.terrainSubmitCpuMs))
OMNI.test.dumpProfiler("distance32-forward")

OMNI.test.setLook(0, 90)
OMNI.wait(8)

print("frame-profiler down frameMs=" .. tostring(OMNI.client.state.frameTimeMs) ..
    " resident=" .. tostring(OMNI.client.state.residentMeshCount) ..
    " candidates=" .. tostring(OMNI.client.state.visibilityCandidates) ..
    " presented=" .. tostring(OMNI.client.state.presentedMeshCount) ..
    " findVisibleMs=" .. tostring(OMNI.client.state.findVisibleMs) ..
    " submitMs=" .. tostring(OMNI.client.state.terrainSubmitCpuMs))
OMNI.test.dumpProfiler("distance32-down")

assert(OMNI.client.state.webGpuErrorCount == 0, "WebGPU validation failed")
OMNI.test.pass()
