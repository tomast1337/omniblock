-- The scale profile is restricted to E2E launches and must be selected before world creation so
-- the client, integrated server, transport identity, and persistent cache all use L7.
assert(OMNI.test.configureTerrainLodScaleProfile(512), "could not select the 512-chunk scale profile")
OMNI.config.viewDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.test.teleport(0, 192, 320)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady and OMNI.client.state.playerY > 180
end, 45, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

assert(OMNI.test.prepareTerrainLodFixture(512), "could not queue the 512-chunk fixture")
OMNI.waitUntil(function()
    return OMNI.test.terrainLodFixtureMetric("complete") == 1 or
        OMNI.test.terrainLodFixtureMetric("failed") == 1
end, 300, 0.05)
assert(OMNI.test.terrainLodFixtureMetric("failed") == 0, "server could not prepare the fixture")
assert(OMNI.test.terrainLodFixtureMetric("tiles") > 0, "fixture contained no tiles")
print("prepared persistent terrain LOD 512 tiles=" ..
    tostring(OMNI.test.terrainLodFixtureMetric("tiles")))
OMNI.test.pass()
