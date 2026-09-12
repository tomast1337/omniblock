OMNI.config.viewDistance = 0.5
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.test.setFlying(true)

OMNI.waitUntil(function()
    return OMNI.client.state.meshSafetyHoles == 0 and
        OMNI.client.state.meshReadyRadius >= 3
end, 45, 0.05)

local function sampleDirection(pitch)
    OMNI.test.setLook(0, pitch)
    OMNI.wait(1)

    local presented = 0
    local solidDraws = 0
    local translucentDraws = 0
    local frameTime = 0
    local samples = 40
    for _ = 1, samples do
        OMNI.wait(0.05)
        presented += OMNI.client.state.presentedMeshCount
        solidDraws += OMNI.client.state.solidDrawsLastFrame
        translucentDraws += OMNI.client.state.translucentDrawsLastFrame
        frameTime += OMNI.client.state.frameTimeMs
    end

    return {
        presented = presented / samples,
        solidDraws = solidDraws / samples,
        translucentDraws = translucentDraws / samples,
        frameTime = frameTime / samples
    }
end

local horizon = sampleDirection(0)
local down = sampleDirection(90)

print("frustum horizon presented=" .. tostring(horizon.presented) ..
    " solid=" .. tostring(horizon.solidDraws) ..
    " translucent=" .. tostring(horizon.translucentDraws) ..
    " frame=" .. tostring(horizon.frameTime) .. "ms")
print("frustum down presented=" .. tostring(down.presented) ..
    " solid=" .. tostring(down.solidDraws) ..
    " translucent=" .. tostring(down.translucentDraws) ..
    " frame=" .. tostring(down.frameTime) .. "ms")

OMNI.test.dumpTerrain("frustum-down")
-- Presented-section count includes the conservative safety-ring rescue and therefore is not
-- expected to be monotonic with GPU cost yet. This guard proves camera direction reaches terrain
-- selection; the printed frame/draw values are the benchmark used to guide the spatial-culler
-- follow-up without imposing machine-specific timing thresholds in CI.
local presentedDelta = math.abs(down.presented - horizon.presented)
assert(presentedDelta >= math.max(1, horizon.presented * 0.05),
    "camera direction did not materially change terrain selection: horizon=" ..
    tostring(horizon.presented) .. " down=" .. tostring(down.presented))
assert(horizon.frameTime > 0 and down.frameTime > 0, "directional frame timing did not advance")
OMNI.test.pass()
