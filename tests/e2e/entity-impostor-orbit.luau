-- Paired 3D/impostor screenshots at all 26 capture directions around a stationary cow.
-- Force selects a tier for visual inspection only; unsupported poses/providers still use 3D.
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
OMNI.test.teleport(0, 32, 0)
OMNI.waitUntil(function() return OMNI.client.state.playerY > 26 and OMNI.client.state.playerY < 40 end, 10, 0.01)
OMNI.test.setFlying(true)
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 45, 0.05)
OMNI.test.flyPath(0, 32, 0, 0, 32, 0, 0.5)
OMNI.wait(1)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(0, 0)
assert(OMNI.test.entityBaseline("cow", 1, 32))

local views = {}
for ring = -1, 1 do
    local e = ring * math.pi / 4
    for azimuth = 0, 7 do
        local a = azimuth * math.pi / 4
        table.insert(views, {math.sin(a)*math.cos(e), math.sin(e), math.cos(a)*math.cos(e)})
    end
end
table.insert(views, {0, 1, 0})
table.insert(views, {0, -1, 0})
for index, direction in ipairs(views) do
    assert(OMNI.test.entityImpostors(false))
    local x, y, z = direction[1]*20, 32 + direction[2]*20, 32 + direction[3]*20
    OMNI.test.flyPath(OMNI.client.state.playerX, OMNI.client.state.playerY, OMNI.client.state.playerZ, x, y, z, 0.35)
    OMNI.wait(0.5)
    OMNI.test.setLook(math.deg(math.atan2(direction[1], -direction[3])), math.deg(math.asin(direction[2])))
    OMNI.waitUntil(function() return OMNI.client.state.entityLodModelSubmissions == 1 end, 15, 0.02)
    print("impostor orbit view " .. (index-1) .. " reference")
    OMNI.test.screenshot()
    OMNI.wait(1.1)
    assert(OMNI.test.entityImpostors(true, true))
    OMNI.waitUntil(function()
        assert(OMNI.client.state.webGpuErrorCount == 0, "WebGPU validation failed")
        return OMNI.client.state.entityLodImpostorSubmissions == 1
    end, 15, 0.02)
    print("impostor orbit view " .. (index-1) .. " impostor")
    OMNI.test.screenshot()
    OMNI.wait(1.1)
end
assert(OMNI.client.state.webGpuErrorCount == 0)
assert(OMNI.test.entityImpostors(false))
OMNI.test.clearEntityBaseline()
OMNI.test.pass()
