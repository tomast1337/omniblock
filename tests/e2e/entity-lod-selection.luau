-- Phase 1: selection is observable, but every selected entity MUST still render as 3D.
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

local function waitForTier(intended)
    OMNI.waitUntil(function()
        return OMNI.client.state.entityLodObserved == 1 and
            OMNI.client.state.entityLodIntendedImpostors == intended
    end, 30, 0.05)
    assert(OMNI.client.state.entityLodModelSubmissions == 1, "lost the 3D fallback")
    assert(OMNI.client.state.entityLodImpostorSubmissions == 0, "Phase 1 drew an impostor")
    assert(OMNI.client.state.entityLodCapacityFallbacks == 0)
    assert(OMNI.client.state.entityLodInvalidView == 0)
end

assert(OMNI.test.entityBaseline("cow", 1, 32))
waitForTier(0)
assert(OMNI.test.entityBaseline("cow", 1, 120))
waitForTier(1)
OMNI.wait(1)
assert(OMNI.test.beginEntitySample())
OMNI.wait(1)
assert(OMNI.test.endEntitySample("phase1-far-cow"), "3D instance missing despite intended tier")
OMNI.test.screenshot()
OMNI.wait(1.5)

-- Effective projection is a quality constraint even when distance did not change.
OMNI.config.fov = 0
waitForTier(0)
OMNI.config.fov = 1
waitForTier(1)

assert(OMNI.test.entityBaseline("sheep", 1, 120))
waitForTier(0)
assert(OMNI.client.state.entityLodUnsupportedProvider == 1, "fleece was implicitly opted in")
assert(OMNI.client.state.entityLodStateCount == 0, "removed cow retained lifetime state")
OMNI.test.clearEntityBaseline()
OMNI.test.pass()
