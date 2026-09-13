-- Phase 4 paired appearance scenes. Each label records the 3D reference first and the impostor
-- second under the same deterministic client-only entity/environment state.
OMNI.config.viewDistance = 4 / 28
OMNI.config.simulationDistance = 0
OMNI.config.presentationQuality = 2
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.fov = 0
OMNI.config.bobView = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-flat"))
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(0, 16, 0)
OMNI.waitUntil(function() return OMNI.client.state.playerY > 10 and OMNI.client.state.playerY < 24 end, 10, 0.01)
OMNI.test.setFlying(true)
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 45, 0.05)
OMNI.test.flyPath(0, 16, 0, 0, 16, 0, 0.5)
OMNI.wait(1)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(0, 0)

local function pair(label, expected)
    assert(OMNI.test.entityImpostors(false))
    OMNI.waitUntil(function() return OMNI.client.state.entityLodModelSubmissions == expected end, 15, 0.02)
    print("impostor appearance " .. label .. " reference")
    OMNI.test.screenshot()
    OMNI.wait(1.1)
    assert(OMNI.test.entityImpostors(true, true))
    OMNI.waitUntil(function()
        assert(OMNI.client.state.webGpuErrorCount == 0, "WebGPU validation failed")
        assert(OMNI.client.state.entityImpostorFailures == 0, "impostor capture failed")
        return OMNI.client.state.entityLodImpostorSubmissions == expected
    end, 30, 0.01)
    print("impostor appearance " .. label .. " impostor")
    OMNI.test.screenshot()
    OMNI.wait(1.1)
end

-- Use a near inspection distance and force the tier. Actual distance switching remains covered
-- by selector tests; at that size one cow is too small for useful screenshot review at 850x480.
assert(OMNI.test.entityBaseline("cow", 1, 32))
assert(OMNI.test.entityBaselineState("idle"))
assert(OMNI.test.entityBaselineEnvironment("day"))
pair("cow-day-idle", 1)
assert(OMNI.test.entityBaselineEnvironment("night"))
pair("cow-night-idle", 1)
assert(OMNI.test.entityBaselineEnvironment("day"))
assert(OMNI.test.entityBaselineState("hurt"))
pair("cow-day-hurt", 1)
assert(OMNI.test.entityBaselineEnvironment("day"))
assert(OMNI.test.entityBaselineState("walk-2"))
pair("cow-day-walk", 1)

assert(OMNI.test.entityBaseline("zombie", 1, 32))
assert(OMNI.test.entityBaselineState("walk-2"))
pair("zombie-day-walk", 1)

assert(OMNI.test.entityBaseline("creeper", 1, 32))
assert(OMNI.test.entityBaselineState("walk-2"))
pair("creeper-day-walk", 1)

assert(OMNI.test.entityBaseline("wolf", 1, 32))
assert(OMNI.test.entityBaselineState("walk-2"))
pair("wild-wolf-day-walk", 1)

assert(OMNI.test.entityBaseline("sheep", 32, 32))
assert(OMNI.test.entityBaselineEnvironment("night"))
assert(OMNI.test.entityBaselineState("idle"))
pair("sheep-night-colors-and-sheared", 32)
assert(OMNI.client.state.entityImpostorOverlaySubmissions == 16)

assert(OMNI.test.entityImpostors(false))
OMNI.test.clearEntityBaseline()
assert(OMNI.client.state.webGpuErrorCount == 0)
OMNI.test.pass()
