-- Opt-in cow atlas: publication/fallback/lifetime contract; screenshots for visual comparison.
OMNI.config.viewDistance = 4 / 28
OMNI.config.simulationDistance = 0
OMNI.config.presentationQuality = 2
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.fov = 1
OMNI.config.bobView = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(0, 220, 0)
OMNI.waitUntil(function() return OMNI.client.state.playerY > 210 end, 10, 0.01)
OMNI.test.setFlying(true)
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 45, 0.05)
OMNI.test.flyPath(0, 220, 0, 0, 220, 0, 0.5)
OMNI.wait(1)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(0, 0)
assert(OMNI.test.entityBaseline("cow", 1, 120))
OMNI.waitUntil(function() return OMNI.client.state.entityLodIntendedImpostors == 1 end, 30, 0.05)
assert(OMNI.client.state.entityLodModelSubmissions == 1)
OMNI.test.screenshot()
OMNI.wait(1.1)
for replacement = 1, 3 do
    assert(OMNI.test.entityImpostors(true))
    OMNI.waitUntil(function()
        local s = OMNI.client.state
        assert(s.webGpuErrorCount == 0, "WebGPU reported validation errors")
        assert(s.entityImpostorFailures == 0, "capture failed")
        assert(s.entityLodModelSubmissions + s.entityLodImpostorSubmissions == 1, "entity disappeared or doubled")
        if s.entityImpostorViews < 130 then
            assert(s.entityLodImpostorSubmissions == 0, "published a partial atlas")
        end
        return s.entityLodImpostorSubmissions == 1
    end, 20, 0.001)
    assert(OMNI.client.state.entityImpostorReady == 1)
    if replacement == 1 then
        OMNI.test.screenshot(); OMNI.wait(1.1)
        assert(OMNI.test.entityBaselineState("idle"))
        OMNI.waitUntil(function() return OMNI.client.state.entityImpostorPoseMask == 1 end, 5, 0.005)
        for pose = 0, 3 do
            assert(OMNI.test.entityBaselineState("walk-" .. tostring(pose)))
            local expected = 2 ^ (pose + 1)
            OMNI.waitUntil(function()
                return OMNI.client.state.entityImpostorPoseMask == expected and
                    OMNI.client.state.entityLodImpostorSubmissions == 1
            end, 5, 0.005)
        end
        assert(OMNI.test.entityBaselineState("hurt"))
        OMNI.waitUntil(function()
            return OMNI.client.state.entityImpostorHurtSubmissions == 1 and
                OMNI.client.state.entityLodImpostorSubmissions == 1
        end, 5, 0.005)
        assert(OMNI.test.entityBaselineState("idle"))
    end
    assert(OMNI.test.entityImpostors(false))
    OMNI.waitUntil(function() return OMNI.client.state.entityLodModelSubmissions == 1 end, 10, 0.01)
end
assert(OMNI.client.state.entityImpostorReplacements >= 3)
-- One atlas shared by a grid, not a separate capture or draw for each cow.
assert(OMNI.test.entityBaseline("cow", 64, 32))
assert(OMNI.test.entityImpostors(true, true))
OMNI.waitUntil(function() return OMNI.client.state.entityLodImpostorSubmissions == 64 end, 20, 0.02)
assert(OMNI.test.beginEntitySample())
OMNI.wait(1)
assert(OMNI.test.endEntitySample("phase2-64-cows"), "instanced impostor population was incomplete")
assert(OMNI.test.entityImpostors(false))
assert(OMNI.test.entityBaseline("sheep", 1, 120))
assert(OMNI.test.entityImpostors(true))
OMNI.waitUntil(function() return OMNI.client.state.entityLodUnsupportedProvider == 1 end, 15, 0.05)
assert(OMNI.client.state.entityLodImpostorSubmissions == 0)
assert(OMNI.test.entityImpostors(false))
OMNI.test.clearEntityBaseline()
assert(OMNI.client.state.webGpuErrorCount == 0)
OMNI.test.pass()
