-- Synthetic transport/selection regression, NOT evidence of natural-terrain visual quality.
-- Persist coarse horizon coverage plus a finer central partition in a separate process.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0.2
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-smoke"))
local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.teleport(0, 192, 320)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX) < 1 and
        math.abs(state.playerZ - 320) < 1 and state.playerY > 191 and state.playerY < 194.5
end, 45, 0.1)
OMNI.test.setFlying(true)
for _, radius in ipairs({64, 16}) do
    assert(OMNI.test.prepareTerrainLodFixture(radius))
    OMNI.waitUntil(function()
        return OMNI.test.terrainLodFixtureMetric("complete") == 1 or
            OMNI.test.terrainLodFixtureMetric("failed") == 1
    end, 90, 0.1)
    assert(OMNI.test.terrainLodFixtureMetric("failed") == 0, "fixture preparation failed")
end
OMNI.test.pass()
