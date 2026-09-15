-- Every shipped living-mob renderer must reach an actual GPU impostor submission. This catches
-- descriptors that compile but are not attached by specialized renderers such as flapping,
-- glowing-eyes, swimming, scaled, or squishy.
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
OMNI.waitUntil(function() return OMNI.client.state.playerY > 10 end, 10, 0.01)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 0)
OMNI.test.setMovement(0, 0, 0)

local mobs = {
    "chicken", "cow", "creeper", "ghast", "giant", "pig", "pigzombie", "sheep",
    "skeleton", "slime", "spider", "squid", "wolf", "zombie"
}

assert(OMNI.test.entityImpostors(true, true))
for _, mob in ipairs(mobs) do
    assert(OMNI.test.entityBaseline(mob, 1, 32), "could not create " .. mob .. " fixture")
    assert(OMNI.test.entityBaselineState("idle"))
    OMNI.waitUntil(function()
        local s = OMNI.client.state
        assert(s.webGpuErrorCount == 0, "WebGPU validation failed for " .. mob)
        assert(s.entityImpostorFailures == 0, "impostor bake failed for " .. mob)
        if s.entityLodObserved == 1 then
            assert(s.entityLodModelSubmissions + s.entityLodImpostorSubmissions == 1,
                mob .. " disappeared or rendered twice")
        end
        return s.entityLodImpostorSubmissions == 1
    end, 35, 0.01)
    print("built-in impostor ready: " .. mob)
    OMNI.test.screenshot()
    OMNI.wait(1.1)
end

assert(OMNI.client.state.entityImpostorResidentAtlases == #mobs)
assert(OMNI.test.entityImpostors(false))
OMNI.test.clearEntityBaseline()
assert(OMNI.client.state.webGpuErrorCount == 0)
OMNI.test.pass()
