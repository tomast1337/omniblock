-- Real terrain in the disposable fixture: uncovered, half covered, fully covered cow.
-- Screenshot pairs are reference 3D then forced prototype; GPU counters alone cannot prove occlusion.
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
OMNI.test.setLook(0, 0)
OMNI.test.setMovement(0, 0, 0)
assert(OMNI.test.entityBaseline("cow", 1, 32))
for stage = 0, 2 do
    if stage > 0 then
        local x = stage == 1 and 0 or -1
        for y = 15, 18 do OMNI.test.setBlock("omniblock:stone", x, y, 30) end
        OMNI.wait(1)
        OMNI.waitUntil(function() return OMNI.test.isMeshCurrent(x, 16, 30) end, 30, 0.05)
    end
    assert(OMNI.test.entityImpostors(false))
    OMNI.waitUntil(function() return OMNI.client.state.entityLodModelSubmissions == 1 end, 15, 0.05)
    print("impostor occlusion stage " .. stage .. " reference")
    OMNI.test.screenshot()
    OMNI.wait(1.1)
    assert(OMNI.test.entityImpostors(true, true))
    OMNI.waitUntil(function()
        assert(OMNI.client.state.webGpuErrorCount == 0)
        return OMNI.client.state.entityLodImpostorSubmissions == 1
    end, 15, 0.02)
    print("impostor occlusion stage " .. stage .. " impostor")
    OMNI.test.screenshot()
    OMNI.wait(1.1)
end
assert(OMNI.client.state.webGpuErrorCount == 0)
assert(OMNI.test.entityImpostors(false))
OMNI.test.clearEntityBaseline()
OMNI.test.pass()
