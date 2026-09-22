-- Regression: upgrading local LOD from 0.75x to 1x must compile L0 even after the
-- gameplay chunk has left the exact render radius. The camera and source stay fixed.
OMNI.config.viewDistance = 0.15 -- eight exact chunks to capture the fine source
OMNI.config.terrainHorizonDistance = 0
OMNI.config.terrainLodDropoffDistance = 0.2 -- 0.75x: L1 at the cave column
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false

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
node("#world.create.name").text = "LOD Quality Upgrade"
click("#world.create.more")
node("#world.create.seed").text = "246813579"
click("#world.create.submit")

local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.teleport(24, 115, -70)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 24) < 1 and
        math.abs(state.playerZ + 70) < 1 and state.playerY > 114
end, 45, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 22)

-- Column (1,2) is 110 blocks from the fixed camera: L1 at 0.75x, L0 at 1x.
OMNI.waitUntil(function()
    return OMNI.test.terrainLodColumnSourceLoaded(1, 2) and
        OMNI.test.terrainLodColumnMinimumLevel(1, 2) == 1
end, 90, 0.1)
OMNI.config.viewDistance = 0 -- four exact chunks; column (1,2) must unload
OMNI.waitUntil(function()
    return not OMNI.test.terrainLodColumnSourceLoaded(1, 2) and
        OMNI.test.terrainLodColumnMinimumLevel(1, 2) == 1
end, 45, 0.1)
OMNI.test.dumpTerrain("quality-upgrade-before")

OMNI.config.terrainLodDropoffDistance = 0.4 -- 1x; request retained L0 source
OMNI.waitUntil(function()
    return OMNI.test.terrainLodColumnMinimumLevel(1, 2) == 0
end, 45, 0.1)
assert(not OMNI.test.terrainLodColumnSourceLoaded(1, 2),
    "quality upgrade loaded gameplay terrain solely to refine LOD")
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "render failure during unloaded-source quality upgrade")
OMNI.test.dumpTerrain("quality-upgrade-after")
OMNI.test.pass()
