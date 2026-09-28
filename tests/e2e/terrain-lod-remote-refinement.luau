-- A stationary client must refine remotely sourced terrain, not only visited gameplay chunks.
-- This tests refinement reaching the published partition; the source is deliberately synthetic.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0.2
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
assert(OMNI.client.worlds.load("e2e-smoke"))
local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.teleport(0, 192, 320)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX) < 1 and
        math.abs(state.playerZ - 320) < 1 and state.playerY > 191 and state.playerY < 194.5
end, 45, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(-90, 40)

local observations = 0
local refined, failure = pcall(function()
    OMNI.waitUntil(function()
        local material, sample, owner = OMNI.test.terrainLodPresentedMaterial(136, 63, 280)
        observations += 1
        if observations % 100 == 0 then
            OMNI.test.dumpTerrain("remote-refinement-progress")
            print("remote refinement: ready=" .. tostring(OMNI.test.terrainLodClientTileReady(2, 2, 4)) ..
                " sample=" .. tostring(sample) .. " owner=" .. tostring(owner) ..
                " requests=" .. tostring(state.terrainLodRemoteRequests) ..
                " received=" .. tostring(state.terrainLodRemoteTiles))
        end
        return OMNI.test.terrainLodClientTileReady(2, 2, 4) and
            material ~= nil and sample == 1 and owner == "spatial"
    end, 120, 0.1)
end)
OMNI.test.dumpTerrain("remote-refinement")
OMNI.test.dumpProfiler("remote-refinement")
assert(refined, "nearby remote tile never refined to a presented 1x1 sample: " .. tostring(failure))
assert(not OMNI.test.terrainLodColumnSourceLoaded(8, 17), "target loaded as gameplay terrain")
assert(OMNI.test.terrainLodFixtureMetric("cacheReadHits") > 0, "no persisted source was reopened")
assert(math.abs(state.playerX) < 1 and math.abs(state.playerZ - 320) < 1 and state.playerY > 190,
    "test camera moved or fell")
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0, "render failure")
OMNI.test.pass()
