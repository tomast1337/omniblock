-- Ordinary hostiles have a gameplay despawn radius of 128 blocks. Verify that one remains tracked
-- and presented in the impostor band below that boundary; this must not be conflated with the
-- separate long-range passive-mob persistence contract.
OMNI.config.viewDistance = 12 / 28 -- 16 chunks
OMNI.config.simulationDistance = 0 -- 2 chunks
OMNI.config.presentationQuality = 1
OMNI.config.entityImpostorDistance = 2 / 15 -- 64 blocks
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 90, 0.05)
OMNI.test.creative()
OMNI.test.setFlying(true)
OMNI.wait(0.5)

local before = OMNI.test.countEntities("omniblock:creeper", 0, 16)
assert(OMNI.test.summon("creeper", 1))
OMNI.waitUntil(function()
    return OMNI.test.countEntities("omniblock:creeper", 0, 16) > before
end, 20, 0.02)

OMNI.test.teleport(0, 100, -96)
OMNI.waitUntil(function() return OMNI.client.state.playerZ < -90 end, 10, 0.02)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 8)
OMNI.waitUntil(function()
    return OMNI.test.countEntities("omniblock:creeper", 80, 112) >= 1
end, 20, 0.02)
OMNI.waitUntil(function()
    local state = OMNI.client.state
    return state.entityLodObserved >= 1 and state.entityLodImpostorSubmissions >= 1
end, 30, 0.02)

assert(OMNI.client.state.webGpuErrorCount == 0)
OMNI.test.pass()
