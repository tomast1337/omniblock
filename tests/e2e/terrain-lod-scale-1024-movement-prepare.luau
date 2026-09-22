-- Persist every integer chunk center along the flight route and the teleport destination.
-- This process exits before the measured run; no resident server tiles are carried across.
assert(OMNI.test.configureTerrainLodScaleProfile(1024))
OMNI.config.viewDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(0, 192, 320)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady and
        math.abs(OMNI.client.state.playerX) < 1 and
        math.abs(OMNI.client.state.playerZ - 320) < 1 and
        OMNI.client.state.playerY > 180
end, 45, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

local function prepare(x, z)
    assert(OMNI.test.prepareTerrainLodFixture(1024, x, z), "fixture request rejected")
    OMNI.waitUntil(function()
        return OMNI.test.terrainLodFixtureMetric("complete") == 1 or
            OMNI.test.terrainLodFixtureMetric("failed") == 1
    end, 120, 0.05)
    assert(OMNI.test.terrainLodFixtureMetric("failed") == 0, "fixture preparation failed")
    assert(OMNI.test.terrainLodFixtureMetric("tiles") <= 512, "unbounded fixture partition")
end

for chunk = 0, 10 do prepare(0, 320 + chunk * 16) end
prepare(160, 640)
print("prepared persistent 1024 movement route: 10 flight chunks plus teleport/recenter")
OMNI.test.pass()
