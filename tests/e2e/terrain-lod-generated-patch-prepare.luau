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
    radiusChunks = 32,
    shape = "circle",
    profile = "play",
})
local samples = 0
local generated, generationFailure = pcall(function()
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
    end, 900, 0.1)
end)
if not generated then
    OMNI.test.dumpTerrain("generated-patch-generation-failed")
    OMNI.test.dumpProfiler("generated-patch-generation-failed")
end
assert(generated, "real-source generation failed: " .. tostring(generationFailure))
local p = job.progress
assert(p.preparedTargets > 0 and p.writtenChunks > 0, "cold fixture did not generate terrain")
assert(p.radiusChunks == 32 and p.totalTargets == 3209, "wrong generated-patch footprint")
assert(p.remainingTargets == 0 and p.nextTarget == p.totalTargets)
assert(p.generatorProfile ~= "omniblock:flat", "wrong world generator")
-- Cache entries alone can be satisfied by spawn data. Wait for all admitted conversions,
-- parent construction and persistence (including in-flight disk writes) to become quiet.
local quietSamples = 0
local ok, failure = pcall(function()
    OMNI.waitUntil(function()
        assert(OMNI.test.terrainLodFixtureMetric("preparationFailures") == 0,
            "server LOD pipeline failed")
        assert(OMNI.test.terrainLodFixtureMetric("offlineDropped") == 0,
            "offline terrain was dropped before LOD conversion")
        assert(OMNI.test.terrainLodFixtureMetric("persistenceDeferred") == 0,
            "LOD records were not admitted to persistence")
        local pending = OMNI.test.terrainLodFixtureMetric("preparationPending")
        quietSamples = pending == 0 and quietSamples + 1 or 0
        return quietSamples >= 10 and
            OMNI.test.terrainLodFixtureMetric("offlineSubmitted") >= p.writtenChunks
    end, 60, 0.1)
end)
OMNI.test.dumpTerrain("generated-patch-prepared")
OMNI.test.dumpProfiler("generated-patch-prepared")
assert(ok, "LOD preparation failed: " .. tostring(failure))
print("real generated patch complete: profile=" .. p.generatorProfile ..
    " targets=" .. p.totalTargets .. " written=" .. p.writtenChunks)
OMNI.test.pass()
