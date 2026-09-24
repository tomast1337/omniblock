-- Reopen the naturally generated cave patch prepared in a separate process.
OMNI.config.viewDistance = 0 -- four exact chunks
OMNI.config.terrainHorizonDistance = 0 -- sixteen-chunk LOD horizon
OMNI.config.terrainLodDropoffDistance = 0.2 -- 0.75x: a near, block-scale spatial band
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.fov = 0 -- 30-degree diagnostic view

local function waitFor(predicate, seconds, label)
    local ok, reason = pcall(function() OMNI.waitUntil(predicate, seconds, 0.1) end)
    if ok then return end
    OMNI.test.dumpTerrain(label .. "-not-ready")
    error(tostring(reason))
end

local function capture(label)
    -- The dump describes the presentation immediately before the requested screenshot frame.
    -- Screenshots use second-resolution filenames, so keep captures at least a second apart.
    OMNI.test.dumpTerrain(label)
    OMNI.test.screenshot()
    OMNI.wait(1.2)
end

local caveWorld
for _, world in ipairs(OMNI.client.worlds.list()) do
    if world.name == "LOD Remote Cave Mouth" then caveWorld = world.id end
end
assert(caveWorld ~= nil and OMNI.client.worlds.load(caveWorld),
    "prepared natural-cave world is missing")

local state = OMNI.client.state
waitFor(function() return state.worldLoaded and state.playerReady end, 75, "world")
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.teleport(532, 115, -120)
waitFor(function()
    return state.playerReady and math.abs(state.playerX - 532) < 1 and
        math.abs(state.playerZ + 120) < 1 and state.playerY > 114
end, 45, "remote-camera")
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 18)
capture("remote-cave-arrival")

waitFor(function() return OMNI.test.terrainLodServerTileReady(2, 8, 0) end,
    60, "server-cave-tile")
capture("remote-cave-source-ready")

OMNI.wait(5)
-- Decoration can finish a tree over the cave mouth before the second process
-- opens it. Compare the same published block in LOD and exact terrain without
-- assuming the mouth remains air after cross-border decoration.
local probeX, probeY, probeZ = 532, 70, 22
local probeMaterial
waitFor(function()
    local material, sampleSize, owner =
        OMNI.test.terrainLodPresentedMaterial(probeX, probeY, probeZ)
    if material ~= nil and sampleSize == 1 and owner == "spatial" then
        probeMaterial = material
        return true
    end
    return false
end, 90, "spatial-cave-tile")
print("remote cave tile sample:", probeX, probeY, probeZ, probeMaterial)
capture("remote-cave-first-spatial")
OMNI.wait(5)
capture("remote-cave-mouth")

-- The prepared world has the same source patch in both captures. If the black
-- region remains at the same camera with detailed chunks, it is not an L2-only
-- presentation defect. Do not change FOV, look direction, or player position.
OMNI.config.viewDistance = 0.36 -- fourteen exact chunks; the cave is about nine away
OMNI.wait(0.2) -- let the new radius reach the render frame before the first takeover sample
capture("remote-cave-exact-request")
waitFor(function()
    return OMNI.test.hasBlock(probeMaterial, probeX, probeY, probeZ) and
        OMNI.test.isMeshCurrent(probeX, probeY, probeZ)
end, 90, "exact-cave")
capture("remote-cave-first-exact")
OMNI.wait(5)
capture("remote-cave-mouth-exact")
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "render failure during remote cave-mouth comparison")
OMNI.test.pass()
