-- Persist the natural cave's source chunks before measuring its exact/LOD handoff.
-- This process exits; the comparison process must reopen the saved terrain.
OMNI.config.viewDistance = 0.15 -- eight exact chunks include every source chunk of L2 tile (0,0)
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end
local function click(selector)
    assert(node(selector):click(), "could not click UI node: " .. selector)
end

assert(OMNI.ui.screen == "MainMenuScreen")
click("#main.singleplayer")
OMNI.waitUntil(function() return OMNI.ui.screen == "WorldScreen" end, 5)
click("#world.create")
OMNI.waitUntil(function() return OMNI.ui.screen == "CreateWorldScreen" end, 5)
node("#world.create.name").text = "LOD Natural Cave Stable"
click("#world.create.more")
node("#world.create.seed").text = "246813579"
click("#world.create.submit")

local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.teleport(24, 115, 33)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 24) < 1 and
        math.abs(state.playerZ - 33) < 1 and state.playerY > 114
end, 45, 0.1)
OMNI.test.setFlying(true)
-- Tile (0,0) contains the stone-roof opening at (24,76,33). Wait for the exact
-- source neighborhood, not L2 publication: a cold tile build can take longer than
-- source loading and should be diagnosed in the fresh measurement process.
OMNI.waitUntil(function()
    return OMNI.test.hasBlock("omniblock:air", 24, 76, 33) and
        OMNI.test.isMeshCurrent(24, 76, 33) and
        state.terrainCoverageReady == 1 and state.terrainCoverageExpected > 0 and
        state.terrainCoverageMissingChunkData == 0 and
        state.terrainCoverageMissingExactMeshes == 0 and
        state.terrainCoverageMissingPresentation == 0 and
        state.terrainCoverageHoles == 0 and state.terrainCoverageOverlaps == 0
end,
    120, 0.1)
OMNI.test.dumpTerrain("natural-cave-stable-prepared")
OMNI.test.pass()
