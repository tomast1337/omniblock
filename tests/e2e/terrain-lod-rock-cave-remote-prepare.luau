-- Persist the natural rock-cave patch before the comparison client starts.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

local function waitFor(predicate, seconds, label)
    local ok, reason = pcall(function() OMNI.waitUntil(predicate, seconds, 0.1) end)
    if ok then return end
    OMNI.test.dumpTerrain(label .. "-not-ready")
    error(tostring(reason))
end

local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end

assert(OMNI.ui.screen == "MainMenuScreen")
assert(node("#main.singleplayer"):click())
waitFor(function() return OMNI.ui.screen == "WorldScreen" end, 5, "world-screen")
assert(node("#world.create"):click())
waitFor(function() return OMNI.ui.screen == "CreateWorldScreen" end, 5, "create-screen")
node("#world.create.name").text = "LOD Rock Cave Remote"
assert(node("#world.create.more"):click())
node("#world.create.seed").text = "246813579"
assert(node("#world.create.submit"):click())

local state = OMNI.client.state
waitFor(function() return state.worldLoaded and state.playerReady end, 75, "world")
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

-- The 4x4-chunk L2 tile (0,0) contains the stone-roof opening at (24,76,33).
-- It is inside the server's ordinary prepared spawn region, so a separate
-- worldgen job would duplicate work and compete with spawn chunk preparation.
waitFor(function() return OMNI.test.terrainLodServerTileReady(2, 0, 0) end,
    90, "server-rock-cave-tile")
OMNI.test.dumpTerrain("rock-cave-prepared")
OMNI.test.pass()
