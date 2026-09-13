-- Opt-in presentation benchmark, not a spawning/tracking/simulation benchmark.
-- Each pass writes raw per-frame samples + resource hashes. Screenshots happen AFTER sampling.
OMNI.config.viewDistance = 4 / 28 -- eight chunks: fixture at 32/64 blocks is inside the cap
OMNI.config.simulationDistance = 0
OMNI.config.presentationQuality = 2
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.fov = 0.44444445
OMNI.config.bobView = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-flat"), "could not launch flat fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(0, 16, 0)
OMNI.waitUntil(function()
    return math.abs(OMNI.client.state.playerX) < 2 and
        OMNI.client.state.playerY > 10 and OMNI.client.state.playerY < 24 and
        math.abs(OMNI.client.state.playerZ) < 2
end, 10, 0.01)
OMNI.test.setFlying(true)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady
end, 45, 0.05)
-- Normalize the endpoint after the server teleport's stance adjustment.
OMNI.test.flyPath(0, 16, 0, 0, 16, 0, 0.5)
OMNI.wait(1)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(0, 0)

local stable = 0
OMNI.waitUntil(function()
    if OMNI.client.state.meshPending == 0 and OMNI.client.state.meshSafetyHoles == 0 then
        stable += 1
    else stable = 0 end
    return stable >= 30
end, 120, 0.1)

local cases = {
    {"empty", 0, 32}, {"cow", 1, 16}, {"cow", 64, 32},
    {"sheep", 64, 32}, {"mixed", 64, 32}, {"mixed", 256, 64}
}
for _, case in ipairs(cases) do
    local scene, count, distance = case[1], case[2], case[3]
    local label = scene .. "-" .. tostring(count) .. "-d" .. tostring(distance)
    assert(OMNI.test.entityBaseline(scene, count, distance), "fixture rejected: " .. label)
    OMNI.wait(2) -- warm model baking, pipeline creation, texture uploads, JIT
    assert(OMNI.test.beginEntitySample())
    OMNI.wait(5)
    assert(OMNI.test.endEntitySample(label), "invalid sample (see JSON): " .. label)
    print("entity-baseline screenshot: " .. label)
    OMNI.test.screenshot()
    OMNI.wait(1.5) -- async capture, outside timed samples; filenames remain in capture order
end
OMNI.test.clearEntityBaseline()
assert(OMNI.client.state.meshBuildFailureCount == 0, "mesh build failed during baseline")
OMNI.test.pass()
