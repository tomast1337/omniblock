-- Exact and authoritative remote L2 views of the same persisted natural cave. The separate
-- preparation process persisted the cave tile, not every chunk visible in the screenshot.
OMNI.config.viewDistance = 0.15 -- eight exact chunks
OMNI.config.terrainHorizonDistance = 0 -- sixteen-chunk horizon
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.cloudsQuality = 1
OMNI.config.fov = 0 -- 30-degree cave view

local caveWorld
for _, world in ipairs(OMNI.client.worlds.list()) do
    if world.name == "LOD Natural Cave Stable" then caveWorld = world.id end
end
assert(caveWorld ~= nil and OMNI.client.worlds.load(caveWorld),
    "prepared natural-cave world is missing")

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
end, 60, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 22)

local function covered()
    return state.terrainCoverageReady == 1 and state.terrainCoverageExpected > 0 and
        state.terrainCoverageMissingChunkData == 0 and
        state.terrainCoverageMissingExactMeshes == 0 and
        state.terrainCoverageMissingPresentation == 0 and
        state.terrainCoverageHoles == 0 and state.terrainCoverageOverlaps == 0 and
        state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0
end

local caveX, caveY, caveZ = 24, 76, 33
OMNI.waitUntil(function()
    return covered() and OMNI.test.hasBlock("omniblock:air", caveX, caveY, caveZ) and
        OMNI.test.isMeshCurrent(caveX, caveY, caveZ)
end, 120, 0.1)
local roofY
for y = caveY + 1, caveY + 8 do
    if OMNI.test.hasBlock("omniblock:stone", caveX, y, caveZ) then
        roofY = y
        break
    end
end
assert(roofY ~= nil, "natural cave lost its stone roof")
OMNI.wait(2)
OMNI.test.dumpTerrain("natural-cave-stable-exact")
OMNI.test.screenshot()
OMNI.wait(10) -- screenshot toast must not contaminate the second capture

OMNI.config.viewDistance = 0 -- four exact chunks; the cave now needs remote L2
OMNI.waitUntil(function()
    local material, sampleSize, owner, skyLight, _, _, presented =
        OMNI.test.terrainLodPresentedSample(caveX, caveY, caveZ)
    local roof, roofSize, roofOwner, _, _, roofOpaque =
        OMNI.test.terrainLodPresentedSample(caveX, roofY, caveZ)
    return covered() and material == "omniblock:air" and
        presented == "omniblock:air" and sampleSize == 1 and
        owner == "spatial" and skyLight >= 8 and
        roof == "omniblock:stone" and roofSize == 1 and
        roofOwner == "spatial" and roofOpaque and
        state.terrainLodSpatialSubmissionReady == 1 and
        state.terrainLodSpatialSolidPages > 0
end, 180, 0.1)
OMNI.wait(2)
OMNI.test.dumpTerrain("natural-cave-stable-spatial")
OMNI.test.screenshot()
OMNI.wait(0.5)
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "render failure during stable natural-cave comparison")
OMNI.test.pass()
