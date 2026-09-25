-- The runner moves the prepared world's derived LOD cache aside before this
-- process starts. No movement or teleport may be needed to import saved terrain.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0.2 -- 64-chunk horizon
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.1)
OMNI.test.creative()
OMNI.test.setMovement(0, 0, 0)

local state = OMNI.client.state
local startX, startZ = state.playerX, state.playerZ
local delivered, deliveryFailure = pcall(function()
    OMNI.waitUntil(function()
        return OMNI.test.terrainLodClientTileReady(3, 4, 4)
    end, 180, 0.1)
end)
if not delivered then
    OMNI.test.dumpTerrain("saved-cold-client-not-ready")
    OMNI.test.dumpProfiler("saved-cold-client-not-ready")
end
assert(delivered, "saved terrain did not reach a GPU-ready client tile: " ..
    tostring(deliveryFailure))
assert(OMNI.test.terrainLodServerTileReady(3, 4, 4),
    "client tile has no server-approved saved source")
assert(math.abs(state.playerX - startX) < 1 and math.abs(state.playerZ - startZ) < 1,
    "player moved during saved-terrain import")
assert(not OMNI.test.terrainLodColumnSourceLoaded(36, 36),
    "distant source area was loaded as gameplay chunks")
assert(OMNI.test.terrainLodFixtureMetric("savedChunksImported") >= 64,
    "target tile came from an old cache instead of the saved chunks")
assert(OMNI.test.terrainLodFixtureMetric("savedImportsFailed") == 0,
    "saved-terrain import failed")
print("stationary saved LOD L3 (4,4) ready; imported " ..
    tostring(OMNI.test.terrainLodFixtureMetric("savedChunksImported")) .. " source chunks")
OMNI.test.dumpTerrain("saved-cold-ready")
OMNI.test.dumpProfiler("saved-cold-ready")
OMNI.test.pass()
