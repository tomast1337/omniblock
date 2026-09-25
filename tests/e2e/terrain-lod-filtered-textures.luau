-- Opt-in visual gate for the first coarse spatial tier (2x2 source samples). It generates a
-- bounded natural patch, then observes it from outside the patch so L3 must own visible terrain.
OMNI.config.viewDistance = 0 -- four exact chunks
OMNI.config.terrainHorizonDistance = 0.06666667 -- 32 chunks; L3 cannot be selected at 16
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.cloudsQuality = 1
OMNI.config.useMipmaps = true

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
    radiusChunks = 12,
    shape = "circle",
    profile = "play",
})
OMNI.waitUntil(function()
    local progress = job.progress
    if progress == nil then return false end
    assert(progress.status ~= "failed", "source generation failed: " ..
        tostring(progress.lastError))
    return progress.status == "completed"
end, 300, 0.1)

OMNI.test.teleport(576, 170, 752)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 576) < 1 and
        math.abs(state.playerZ - 752) < 1 and state.playerY > 169
end, 60, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(180, 55)
local l3Ready, l3Failure = pcall(function() OMNI.waitUntil(function()
    return state.terrainLodSpatialSubmissionReady == 1 and
        state.terrainLodSpatialHighestAuthoritativeLevel >= 3 and
        state.terrainLodSpatialSolidPages > 0 and
        state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0
end, 120, 0.1) end)
OMNI.wait(2)
OMNI.test.dumpTerrain("filtered-textures-l3")
OMNI.test.screenshot()
assert(l3Ready,
    "L3 was not presented: " .. tostring(l3Failure) ..
    " authoritative=" .. tostring(state.terrainLodSpatialHighestAuthoritativeLevel) ..
    " resident=" .. tostring(state.terrainLodSpatialGpuResident) ..
    " pages=" .. tostring(state.terrainLodSpatialSolidPages) ..
    " coarseComplete=" .. tostring(state.terrainLodCoarseComplete))
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0,
    "coarse terrain texture rendering failed")
OMNI.test.pass()
