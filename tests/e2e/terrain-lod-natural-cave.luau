-- Opt-in visual comparison on actual seed terrain. The coordinate is guarded by the
-- generated-terrain fidelity test; it is not a hand-placed arch or synthetic LOD tile.
OMNI.config.viewDistance = 0.15 -- eight exact chunks for the reference
OMNI.config.terrainHorizonDistance = 0 -- sixteen-chunk LOD horizon
OMNI.config.terrainLodDropoffDistance = 0.4 -- 1x: compile the 128-block fine band first
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.fov = 0.44444445 -- fixed 70-degree reference

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
node("#world.create.name").text = "LOD Natural Cave"
click("#world.create.more")
node("#world.create.seed").text = "246813579"
click("#world.create.submit")

local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
-- Exact radius eight includes all four chunk rows of L2 tile (0,0); radius four
-- excludes them. The camera never moves during the comparison.
OMNI.test.teleport(24, 115, -70)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 24) < 1 and
        math.abs(state.playerZ + 70) < 1 and state.playerY > 114
end, 45, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 22)
OMNI.wait(20) -- let normal chunk generation and L2 conversion reach the server cache
OMNI.test.dumpTerrain("natural-cave-exact")
OMNI.test.screenshot()
OMNI.wait(10) -- let the screenshot notification clear before the LOD image
OMNI.config.fov = 0 -- same camera, 30-degree close-up of the natural opening
OMNI.wait(3)
OMNI.test.dumpTerrain("natural-cave-exact-zoom")
OMNI.test.screenshot()
OMNI.wait(10)
OMNI.config.fov = 0.44444445

OMNI.config.viewDistance = 0 -- same camera, four exact chunks
-- This opening belongs to a retained local column after the exact-radius shrink.
-- Remote spatial ownership is unrelated to this comparison and may legitimately
-- remain zero; the artifact checker verifies that the local cave layer is drawn.
OMNI.wait(15)
OMNI.test.dumpTerrain("natural-cave-fine-before")

-- Switch only presentation quality after L0 was compiled from the live source.
-- This isolates selection cost; a low-first, source-unloaded quality upgrade is a
-- separate lifecycle gap, not a valid apples-to-apples geometry measurement.
OMNI.config.terrainLodDropoffDistance = 0.2 -- 0.75x: 96-block near band
OMNI.wait(5)
OMNI.test.dumpTerrain("natural-cave-coarse")
OMNI.test.screenshot()
OMNI.wait(10) -- keep the screenshot toast out of the fine-detail capture

-- Switch back to the original selection. The source and camera stay fixed.
OMNI.config.terrainLodDropoffDistance = 0.4 -- 1x: 128-block near band
OMNI.wait(5)
OMNI.test.dumpTerrain("natural-cave-lod")
OMNI.test.dumpProfiler("natural-cave-lod")
OMNI.test.screenshot()
OMNI.wait(10)
OMNI.config.fov = 0
OMNI.wait(3)
OMNI.test.dumpTerrain("natural-cave-lod-zoom")
OMNI.test.screenshot()
OMNI.wait(0.5)
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "render failure during natural-cave comparison")
OMNI.test.pass()
