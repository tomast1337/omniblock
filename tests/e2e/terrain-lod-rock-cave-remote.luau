-- Reopen the persisted seed tile so neither the live generator nor retained
-- local meshes can stand in for the remote spatial presentation.
OMNI.config.viewDistance = 0 -- four exact chunks
OMNI.config.terrainHorizonDistance = 0 -- sixteen-chunk horizon
OMNI.config.terrainLodDropoffDistance = 0.2 -- block-scale spatial band
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.fov = 0 -- fixed 30-degree diagnostic view

local function waitFor(predicate, seconds, label)
    local ok, reason = pcall(function() OMNI.waitUntil(predicate, seconds, 0.1) end)
    if ok then return end
    OMNI.test.dumpTerrain(label .. "-not-ready")
    error(tostring(reason))
end

local caveWorld
for _, world in ipairs(OMNI.client.worlds.list()) do
    if world.name == "LOD Rock Cave Remote" then caveWorld = world.id end
end
assert(caveWorld ~= nil and OMNI.client.worlds.load(caveWorld),
    "prepared rock-cave world is missing")

local state = OMNI.client.state
waitFor(function() return state.worldLoaded and state.playerReady end, 75, "world")
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.teleport(24, 115, -120)
waitFor(function()
    return state.playerReady and math.abs(state.playerX - 24) < 1 and
        math.abs(state.playerZ + 120) < 1 and state.playerY > 114
end, 45, "remote-camera")
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 14)
waitFor(function() return OMNI.test.terrainLodServerTileReady(2, 0, 0) end,
    60, "server-rock-cave-tile")

local probeX, probeY, probeZ = 24, 76, 33
local roofY, roofMaterial
waitFor(function()
    local material, sampleSize, owner, skyLight, _, _, presented =
        OMNI.test.terrainLodPresentedSample(probeX, probeY, probeZ)
    if material ~= "omniblock:air" or presented ~= "omniblock:air" or
        sampleSize ~= 1 or owner ~= "spatial" or skyLight < 8 then
        return false
    end
    for y = probeY + 1, probeY + 8 do
        local above, aboveSize, aboveOwner, _, _, opaque =
            OMNI.test.terrainLodPresentedSample(probeX, y, probeZ)
        if above == "omniblock:stone" and aboveSize == 1 and aboveOwner == "spatial" and opaque then
            roofY, roofMaterial = y, above
            return true
        end
    end
    return false
end, 90, "spatial-rock-cave")
print("rock cave opening:", probeX, probeY, probeZ, "roof", roofMaterial, roofY)
OMNI.test.dumpTerrain("rock-cave-spatial")
OMNI.test.screenshot()
OMNI.wait(1.2)

-- Keep the same camera and source while exact terrain takes ownership.
OMNI.config.viewDistance = 0.36 -- fourteen exact chunks
waitFor(function()
    return OMNI.test.hasBlock("omniblock:air", probeX, probeY, probeZ) and
        OMNI.test.hasBlock(roofMaterial, probeX, roofY, probeZ) and
        OMNI.test.isMeshCurrent(probeX, probeY, probeZ)
end, 90, "exact-rock-cave")
OMNI.test.dumpTerrain("rock-cave-exact")
OMNI.test.screenshot()
-- A current mesh at the sample does not prove the surrounding sections have
-- presented. Capture a settled frame separately to classify transient gaps.
OMNI.wait(8)
OMNI.test.dumpTerrain("rock-cave-exact-settled")
OMNI.test.screenshot()
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "render failure during rock cave comparison")
OMNI.test.pass()
