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

waitFor(function() return OMNI.test.terrainLodServerTileReady(2, 8, 0) end,
    60, "server-cave-tile")

OMNI.wait(5)
local observedMaterial, observedSampleSize, observedOwner =
    OMNI.test.terrainLodPresentedMaterial(532, 70, 22)
print("remote cave first presentation:", tostring(observedMaterial),
    tostring(observedSampleSize), tostring(observedOwner))
waitFor(function()
    local material, sampleSize, owner = OMNI.test.terrainLodPresentedMaterial(532, 70, 22)
    return material == "omniblock:air" and sampleSize == 1 and owner == "spatial"
end, 90, "spatial-cave")
OMNI.wait(5)
OMNI.test.dumpTerrain("remote-cave-mouth")
OMNI.test.screenshot()
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "render failure during remote cave-mouth capture")
OMNI.test.pass()
