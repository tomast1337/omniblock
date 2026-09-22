-- Bounded real-source gate, NOT a complete 64/512/1024 horizon benchmark. Unknown terrain
-- outside the generated patch must remain unknown rather than receive synthetic flat tiles.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
local state = OMNI.client.state
assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

local prepared = nil
for _, job in OMNI.worldgen.list() do
    if job.id == "e2e_generated_lod_patch" then prepared = job end
end
assert(prepared ~= nil and prepared.status == "completed", "prepared job was not reopened")
assert(prepared.writtenChunks > 0, "preparation did not commit actual chunks")
local tilesBefore = state.terrainLodRemoteTiles
OMNI.test.teleport(1024, 192, 1024)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 1024) < 1 and
        math.abs(state.playerZ - 1024) < 1 and state.playerY > 180
end, 60, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 70)
OMNI.waitUntil(function()
    return state.terrainLodRemoteTiles > tilesBefore and
        OMNI.test.terrainLodFixtureMetric("cacheReadHits") > 0 and
        state.terrainLodSpatialGpuResident > 0 and state.terrainLodSpatialSubmissionReady == 1
end, 120, 0.1)

local function observe()
    assert(state.webGpuErrorCount == 0, "WebGPU validation failed")
    assert(state.meshBuildFailureCount == 0, "exact meshing failed")
    assert(state.terrainLodIdentityMismatches == 0 and
        state.terrainLodIdentityRejectedMessages == 0, "cache identity mismatch")
    assert(state.terrainLodSpatialGpuBytes <= 268435456, "GPU budget exceeded")
    assert(state.terrainLodSpatialMeshQueued <= 32 and
        state.terrainLodSpatialSeamQueued <= 64, "compilation queue budget exceeded")
end
for direction = 0, 7 do
    OMNI.test.setLook(direction * 45, 70)
    for _ = 1, 10 do observe(); OMNI.wait(0.1) end
    OMNI.test.dumpTerrain("generated-patch-rotation-" .. direction)
end
OMNI.test.dumpProfiler("generated-patch")
print("real generated patch: remoteTiles=" .. state.terrainLodRemoteTiles ..
    " cacheHits=" .. OMNI.test.terrainLodFixtureMetric("cacheReadHits") ..
    " resident=" .. state.terrainLodSpatialGpuResident ..
    " missing=" .. state.terrainLodRemoteCoverageMissing)
OMNI.test.pass()
