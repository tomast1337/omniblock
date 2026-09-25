-- A natural-terrain exact/LOD comparison with known source coverage. This is opt-in: generating
-- the bounded patch takes longer than the ordinary smoke suite. Do not infer visual quality from
-- resident tile counts or from an unvisited world's blue, source-less outer horizon.
OMNI.config.viewDistance = 0.15 -- eight exact chunks for the reference
OMNI.config.terrainHorizonDistance = 0 -- sixteen-chunk minimum horizon
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.cloudsQuality = 1 -- off; clouds obscured most of the downward terrain reference
OMNI.config.fov = 0.44444445

assert(OMNI.client.worlds.load("e2e-smoke"))
local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

-- Prepare a circle that contains the complete 8x8 exact-reference square, away from spawn.
-- The generated chunks and their LOD records come from the real default world generator.
local job = OMNI.worldgen.start({
    id = "e2e_lod_visual_baseline",
    dimension = "omniblock:overworld",
    center = { x = 576, z = 576 },
    radiusChunks = 12,
    shape = "circle",
    profile = "play",
})
OMNI.waitUntil(function()
    local progress = job.progress
    if progress == nil then return false end
    assert(progress.status ~= "failed", "visual source generation failed: " ..
        tostring(progress.lastError))
    return progress.status == "completed"
end, 300, 0.1)
assert(job.progress.writtenChunks > 0 and job.progress.remainingTargets == 0,
    "natural terrain source patch is incomplete")

OMNI.test.teleport(576, 170, 576)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 576) < 1 and
        math.abs(state.playerZ - 576) < 1 and state.playerY > 169
end, 60, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 60)

local function ready()
    return state.playerReady and state.terrainCoverageReady == 1 and
        state.terrainCoverageExpected > 0 and
        state.terrainCoverageMissingChunkData == 0 and
        state.terrainCoverageMissingExactMeshes == 0 and
        state.terrainCoverageMissingPresentation == 0 and
        state.terrainCoverageHoles == 0 and state.terrainCoverageOverlaps == 0 and
        state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0
end

OMNI.waitUntil(ready, 90, 0.1)
OMNI.wait(2)
OMNI.test.dumpTerrain("visual-baseline-exact")
OMNI.test.screenshot()
OMNI.wait(0.5)

OMNI.config.viewDistance = 0 -- four exact chunks; the same natural terrain now uses LOD
OMNI.wait(2)
local lodReady, lodFailure = pcall(function()
    OMNI.waitUntil(function()
        return ready() and state.terrainLodSpatialSubmissionReady == 1 and
            state.terrainLodSpatialSolidPages > 0
    end, 90, 0.1)
end)
OMNI.wait(2)
OMNI.test.dumpTerrain("visual-baseline-lod")
OMNI.test.screenshot()
OMNI.wait(0.5)
assert(lodReady,
    "natural LOD failed to converge: " .. tostring(lodFailure) ..
    " coverage=" .. tostring(state.terrainCoverageCovered) .. "/" ..
    tostring(state.terrainCoverageExpected) ..
    " missingData=" .. tostring(state.terrainCoverageMissingChunkData) ..
    " missingMesh=" .. tostring(state.terrainCoverageMissingExactMeshes) ..
    " missingPresentation=" .. tostring(state.terrainCoverageMissingPresentation) ..
    " holes=" .. tostring(state.terrainCoverageHoles) ..
    " spatialReady=" .. tostring(state.terrainLodSpatialSubmissionReady) ..
    " spatialSolidPages=" .. tostring(state.terrainLodSpatialSolidPages))

print("visual baseline: source prepared=" .. tostring(job.progress.writtenChunks) ..
    " remote tiles=" .. tostring(state.terrainLodRemoteTiles) ..
    " near coverage=" .. tostring(state.terrainCoverageCovered) .. "/" ..
    tostring(state.terrainCoverageExpected))
OMNI.test.pass()
