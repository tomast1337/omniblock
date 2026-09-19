-- Preparation half of the fixed-camera scale gate. The runner launches this in the same isolated
-- data directory as the measured process, forcing that second process to reopen durable records.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 1
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.test.teleport(0, 192, 320)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady and OMNI.client.state.playerY > 180 and
        OMNI.client.state.playerZ > 300
end, 45, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

assert(OMNI.test.prepareTerrainLodFixture(64),
    "could not queue the 64-chunk terrain LOD fixture")
OMNI.waitUntil(function()
    return OMNI.test.terrainLodFixtureMetric("complete") == 1 or
        OMNI.test.terrainLodFixtureMetric("failed") == 1
end, 90, 0.05)
assert(OMNI.test.terrainLodFixtureMetric("failed") == 0,
    "server could not prepare the terrain LOD fixture")
assert(OMNI.test.terrainLodFixtureMetric("tiles") > 0,
    "terrain LOD fixture contained no tiles")
print("prepared persistent terrain LOD tiles=" ..
    tostring(OMNI.test.terrainLodFixtureMetric("tiles")))
OMNI.test.pass()
