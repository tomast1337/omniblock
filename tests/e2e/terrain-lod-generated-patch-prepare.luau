-- Cold-source preparation using ordinary world generation, never uniform LOD injection.
-- The runner gives this process and the measured process the same isolated save directory.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

-- Outside the spawn preparation area, while the player stays at spawn. The normal inactive
-- generation/commit path must create the source data, including decoration and lighting.
local job = OMNI.worldgen.start({
    id = "e2e_generated_lod_patch",
    dimension = "omniblock:overworld",
    center = { x = 1024, z = 1024 },
    radiusChunks = 8,
    shape = "circle",
    profile = "play",
})
local samples = 0
OMNI.waitUntil(function()
    local p = job.progress
    if p == nil then return false end
    assert(p.status ~= "failed", "generation failed: " .. tostring(p.lastError))
    samples += 1
    if samples % 50 == 0 then
        print("generated patch: " .. p.nextTarget .. "/" .. p.totalTargets ..
            " written=" .. p.writtenChunks .. " throttle=" .. p.throttleReason)
    end
    return p.status == "completed"
end, 240, 0.1)
local p = job.progress
assert(p.preparedTargets > 0 and p.writtenChunks > 0, "cold fixture did not generate terrain")
assert(p.remainingTargets == 0 and p.nextTarget == p.totalTargets)
assert(p.generatorProfile ~= "omniblock:flat", "wrong world generator")
OMNI.waitUntil(function()
    return OMNI.test.terrainLodFixtureMetric("cacheEntries") > 0
end, 30, 0.1)
OMNI.test.dumpTerrain("generated-patch-prepared")
print("real generated patch complete: profile=" .. p.generatorProfile ..
    " targets=" .. p.totalTargets .. " written=" .. p.writtenChunks)
OMNI.test.pass()
