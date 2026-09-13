-- Phase 5 production-gate benchmark plus motion/zoom/teleport smoke coverage.
-- The timed pair uses the same fixture and settings; cold capture happens before the warmed
-- impostor sample and is reported by that sample's impostorDiagnostics object.
OMNI.config.viewDistance = 4 / 28
OMNI.config.simulationDistance = 0
OMNI.config.presentationQuality = 2
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.fov = 1
OMNI.config.bobView = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.entityImpostors = false

assert(OMNI.client.worlds.load("e2e-flat"))
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(0, 16, 0)
OMNI.waitUntil(function() return OMNI.client.state.playerY > 10 and OMNI.client.state.playerY < 24 end, 10, 0.01)
OMNI.test.setFlying(true)
OMNI.test.flyPath(0, 16, 0, 0, 16, 0, 0.5)
OMNI.wait(1)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(0, 0)

assert(OMNI.test.entityBaseline("cow", 256, 120))
OMNI.waitUntil(function()
    return OMNI.client.state.entityLodObserved == 256 and
        OMNI.client.state.entityLodModelSubmissions == 256
end, 30, 0.02)
OMNI.wait(1) -- warm 3D pipelines/JIT before the paired interval
assert(OMNI.test.beginEntitySample())
OMNI.wait(3)
assert(OMNI.test.endEntitySample("phase5-cow-3d"), "invalid 3D rollout sample")
OMNI.test.screenshot()
OMNI.wait(1.1)

-- Exercise the persistent production option, not the force-tier test override.
OMNI.config.entityImpostors = true
OMNI.waitUntil(function()
    local s = OMNI.client.state
    assert(s.webGpuErrorCount == 0, "WebGPU validation failed during cold bake")
    assert(s.entityImpostorFailures == 0, "cold impostor bake failed")
    assert(s.entityLodModelSubmissions + s.entityLodImpostorSubmissions == 256,
        "entities disappeared or doubled during publication")
    return s.entityLodImpostorSubmissions == 256
end, 45, 0.005)
assert(OMNI.client.state.entityImpostorLastBakeMs > 0)
assert(OMNI.client.state.entityImpostorResidentGpuBytes > 0)
assert(OMNI.client.state.entityImpostorStagingBytes > 0)
assert(OMNI.client.state.entityImpostorDrawBatches == 1)
assert(OMNI.test.beginEntitySample())
OMNI.wait(3)
assert(OMNI.test.endEntitySample("phase5-cow-impostor-warm"), "invalid warmed impostor sample")
OMNI.test.screenshot()
OMNI.wait(1.1)

-- Near entities retain the full model while the production gate remains enabled.
assert(OMNI.test.entityBaseline("cow", 64, 32))
OMNI.config.entityImpostors = false
OMNI.waitUntil(function()
    return OMNI.client.state.entityLodObserved == 64 and
        OMNI.client.state.entityLodModelSubmissions == 64
end, 20, 0.02)
assert(OMNI.test.beginEntitySample())
OMNI.wait(2)
assert(OMNI.test.endEntitySample("phase5-cow-near-disabled"), "near control scene was invalid")
OMNI.config.entityImpostors = true
OMNI.waitUntil(function()
    return OMNI.client.state.entityLodObserved == 64 and
        OMNI.client.state.entityLodModelSubmissions == 64
end, 20, 0.02)
assert(OMNI.test.beginEntitySample())
OMNI.wait(2)
assert(OMNI.test.endEntitySample("phase5-cow-near-enabled"), "near scene regressed")

-- Zoom changes projected size and must return to 3D without losing the entity.
assert(OMNI.test.entityBaseline("cow", 1, 120))
OMNI.waitUntil(function() return OMNI.client.state.entityLodImpostorSubmissions == 1 end, 20, 0.02)
OMNI.config.fov = 0
OMNI.waitUntil(function() return OMNI.client.state.entityLodModelSubmissions == 1 end, 20, 0.02)
OMNI.test.screenshot()
OMNI.wait(1.1)
OMNI.config.fov = 1
OMNI.waitUntil(function() return OMNI.client.state.entityLodImpostorSubmissions == 1 end, 20, 0.02)

-- Movement and teleport invalidate selector history but never remove the fallback/presentation.
OMNI.test.flyPath(0, 16, 0, 12, 20, 8, 1.0)
OMNI.waitUntil(function()
    local s = OMNI.client.state
    return s.entityLodObserved == 1 and
        s.entityLodModelSubmissions + s.entityLodImpostorSubmissions == 1
end, 20, 0.02)
OMNI.test.teleport(0, 16, 0)
OMNI.waitUntil(function() return math.abs(OMNI.client.state.playerX) < 2 end, 10, 0.02)
assert(OMNI.test.entityBaseline("cow", 1, 120))
OMNI.waitUntil(function()
    local s = OMNI.client.state
    return s.entityLodObserved == 1 and
        s.entityLodModelSubmissions + s.entityLodImpostorSubmissions == 1
end, 20, 0.02)

assert(OMNI.client.state.entityLodTransitions >= 0)
assert(OMNI.client.state.entityImpostorStaleResults == 0)
assert(OMNI.client.state.webGpuErrorCount == 0)
OMNI.config.entityImpostors = false
OMNI.test.clearEntityBaseline()
OMNI.test.pass()
