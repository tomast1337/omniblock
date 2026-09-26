-- Diagnose transient water presentation while the camera crosses LOD ownership boundaries.
-- Generate the flight corridor first, so source terrain cannot arrive during the water handoff.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0.06666667
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = true
OMNI.config.cloudsQuality = 1
OMNI.config.useMipmaps = true
OMNI.config.fov = 0

assert(OMNI.client.worlds.load("e2e-smoke"))
local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

local job = OMNI.worldgen.start({
    id = "e2e_lod_water_motion",
    dimension = "omniblock:overworld",
    center = { x = 576, z = 576 },
    radiusChunks = 16,
    shape = "circle",
    profile = "play",
})
OMNI.waitUntil(function()
    local progress = job.progress
    if progress == nil then return false end
    assert(progress.status ~= "failed", "water source generation failed: " ..
        tostring(progress.lastError))
    return progress.status == "completed"
end, 330, 0.1)
assert(job.progress.writtenChunks > 0 and job.progress.remainingTargets == 0,
    "water source patch is incomplete")

OMNI.test.teleport(576, 100, 752)
OMNI.test.setFlying(true)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 576) < 1 and
        math.abs(state.playerZ - 752) < 1 and state.playerY > 95
end, 60, 0.1)
OMNI.test.setLook(180, 30)
OMNI.waitUntil(function()
    return state.terrainLodSpatialSubmissionReady == 1 and
        state.terrainLodSpatialHighestAuthoritativeLevel >= 3 and
        state.terrainLodSpatialTranslucentPages > 0 and
        state.terrainCoverageReady == 1 and state.terrainCoverageHoles == 0 and
        state.terrainCoverageMissingPresentation == 0
end, 150, 0.1)
OMNI.wait(2)

local function capture(label)
    assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
        "render failure during water motion")
    assert(state.terrainCoverageHoles == 0 and
        state.terrainCoverageMissingPresentation == 0,
        "terrain coverage regressed during water motion")
    assert(state.terrainLodSpatialTranslucentPages > 0,
        "water-area translucent LOD pages disappeared during movement")
    OMNI.test.dumpTerrain(label)
    OMNI.test.screenshot()
end

capture("water-motion-near-start")
OMNI.test.flyPath(576, 100, 752, 576, 100, 624, 16)
for index = 1, 28 do
    OMNI.wait(0.5)
    capture("water-motion-near-" .. tostring(index))
end
OMNI.waitUntil(function() return state.playerZ < 625 and state.playerReady end, 25, 0.1)
capture("water-motion-near-end")
OMNI.test.pass()
