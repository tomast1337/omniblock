-- The sky, sun, moon and stars must not depend on near-terrain render distance.
OMNI.config.viewDistance = 0 -- 4 chunks, below the former sky cutoff of 8
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.1)

OMNI.test.setLook(0, -90)
OMNI.wait(1)
OMNI.test.screenshot()
OMNI.test.dumpProfiler("sky-distance4")
OMNI.wait(1)
assert(OMNI.client.state.webGpuErrorCount == 0, "sky caused a WebGPU validation error")
OMNI.test.pass()
