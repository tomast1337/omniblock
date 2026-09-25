-- Prepare a complete distant 8x8-chunk source area while the player stays at spawn.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.1)
OMNI.test.creative()
OMNI.test.setMovement(0, 0, 0)

-- Tile L3 (4,4) covers chunks 32..39 in both axes. Radius eight around its
-- center covers every source chunk without activating that area for gameplay.
local job = OMNI.worldgen.start({
    id = "e2e_saved_cold_lod",
    dimension = "omniblock:overworld",
    center = { x = 576, z = 576 },
    radiusChunks = 8,
    shape = "circle",
    profile = "play",
})
OMNI.waitUntil(function()
    local progress = job.progress
    if progress == nil then return false end
    assert(progress.status ~= "failed",
        "saved source generation failed: " .. tostring(progress.lastError))
    return progress.status == "completed"
end, 300, 0.1)
assert(job.progress.writtenChunks >= 64, "saved source patch is incomplete")
OMNI.test.dumpTerrain("saved-cold-prepared")
OMNI.test.pass()
