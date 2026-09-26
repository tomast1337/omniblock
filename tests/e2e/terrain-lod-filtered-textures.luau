-- Opt-in natural-terrain quality fixture. At Y=350, pitch=65 and 30-degree vertical FOV, the
-- expected E2E viewport projects terrain at Y=0..128 inside the generated 16-chunk circle;
-- the image checker verifies the actual viewport. The camera sees water and land. Capture
-- both color states within one session so the viewport and resident terrain remain comparable.
OMNI.config.viewDistance = 0 -- four exact chunks
OMNI.config.terrainHorizonDistance = 0.06666667 -- 32 chunks; L3 cannot be selected at 16
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.cloudsQuality = 1
OMNI.config.useMipmaps = true
OMNI.config.fov = 0 -- 30 degrees

assert(OMNI.client.worlds.load("e2e-smoke"))
local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

local job = OMNI.worldgen.start({
    id = "e2e_lod_filtered_textures",
    dimension = "omniblock:overworld",
    center = { x = 576, z = 576 },
    radiusChunks = 16,
    shape = "circle",
    profile = "play",
})
OMNI.waitUntil(function()
    local progress = job.progress
    if progress == nil then return false end
    assert(progress.status ~= "failed", "source generation failed: " ..
        tostring(progress.lastError))
    return progress.status == "completed"
end, 330, 0.1)
assert(job.progress.writtenChunks > 0 and job.progress.remainingTargets == 0,
    "natural terrain source patch is incomplete")

OMNI.test.teleport(576, 350, 752)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 576) < 1 and
        math.abs(state.playerZ - 752) < 1 and state.playerY > 349
end, 60, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(180, 65)
local l3Ready, l3Failure = pcall(function() OMNI.waitUntil(function()
    return state.terrainLodSpatialSubmissionReady == 1 and
        state.terrainLodSpatialHighestAuthoritativeLevel >= 3 and
        state.terrainLodSpatialSolidPages > 0 and
        state.terrainCoverageReady == 1 and
        state.terrainCoverageMissingChunkData == 0 and
        state.terrainCoverageMissingExactMeshes == 0 and
        state.terrainCoverageMissingPresentation == 0 and
        state.terrainCoverageHoles == 0 and state.terrainCoverageOverlaps == 0 and
        state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0
end, 150, 0.1) end)
OMNI.wait(2)
if not l3Ready then OMNI.test.dumpTerrain("filtered-textures-l3-failed") end
assert(l3Ready,
    "L3 was not presented: " .. tostring(l3Failure) ..
    " authoritative=" .. tostring(state.terrainLodSpatialHighestAuthoritativeLevel) ..
    " resident=" .. tostring(state.terrainLodSpatialGpuResident) ..
    " pages=" .. tostring(state.terrainLodSpatialSolidPages) ..
    " coarseComplete=" .. tostring(state.terrainLodCoarseComplete))
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "coarse terrain texture rendering failed")

assert(OMNI.test.terrainLodRepresentativeColors(false),
    "could not disable representative colors for paired capture")
OMNI.wait(0.25)
OMNI.test.dumpTerrain("filtered-textures-color-off")
OMNI.test.screenshot()
OMNI.wait(0.5)

assert(OMNI.test.terrainLodRepresentativeColors(true),
    "could not enable representative colors for paired capture")
OMNI.wait(0.25)
OMNI.test.dumpTerrain("filtered-textures-color-on")
OMNI.test.screenshot()
OMNI.wait(0.5)
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "representative-color comparison caused a rendering error")
OMNI.test.pass()
