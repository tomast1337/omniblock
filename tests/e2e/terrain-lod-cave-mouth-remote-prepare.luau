-- Prepare natural seed terrain in a separate, low-render-load process. The runner reopens
-- the same isolated save/cache for terrain-lod-cave-mouth-remote.luau.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end

local function waitFor(predicate, seconds, label)
    local ok, reason = pcall(function() OMNI.waitUntil(predicate, seconds, 0.1) end)
    if ok then return end
    OMNI.test.dumpTerrain(label .. "-not-ready")
    error(tostring(reason))
end

assert(OMNI.ui.screen == "MainMenuScreen")
assert(node("#main.singleplayer"):click())
waitFor(function() return OMNI.ui.screen == "WorldScreen" end, 5, "world-screen")
assert(node("#world.create"):click())
waitFor(function() return OMNI.ui.screen == "CreateWorldScreen" end, 5, "create-screen")
node("#world.create.name").text = "LOD Remote Cave Mouth"
assert(node("#world.create.more"):click())
node("#world.create.seed").text = "246813579"
assert(node("#world.create.submit"):click())

local state = OMNI.client.state
waitFor(function() return state.worldLoaded and state.playerReady end, 75, "world")
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.teleport(532, 115, -300)
waitFor(function()
    return state.playerReady and math.abs(state.playerX - 532) < 1 and
        math.abs(state.playerZ + 300) < 1 and state.playerY > 114
end, 45, "remote-camera")
OMNI.test.setFlying(true)

-- World-space center (536,-24) is chunk (33,-2). Radius eight covers the cave's
-- L2 tile (8,0), the camera-to-cave strip, and adjacent tiles. A single isolated
-- tile makes a misleading screenshot even when its own source is correct.
local job = OMNI.worldgen.start({
    id = "e2e_natural_cave_lod",
    dimension = "omniblock:overworld",
    center = { x = 536, z = -24 },
    radiusChunks = 8,
    shape = "circle",
    profile = "play",
})
local samples = 0
waitFor(function()
    local progress = job.progress
    assert(progress == nil or progress.status ~= "failed",
        "natural pregeneration failed: " .. tostring(progress and progress.lastError))
    samples += 1
    if progress and samples % 100 == 0 then
        print("natural cave preparation", progress.nextTarget, progress.totalTargets,
            progress.throttleReason)
    end
    return progress ~= nil and progress.status == "completed"
end, 240, "natural-generation")
assert(job.progress.remainingTargets == 0 and job.progress.nextTarget == job.progress.totalTargets,
    "natural pregeneration did not finish every target")
waitFor(function() return OMNI.test.terrainLodServerTileReady(2, 8, 0) end,
    60, "server-cave-tile")
OMNI.test.dumpTerrain("natural-cave-prepared")
OMNI.test.pass()
