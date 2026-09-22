-- Real generated spawn terrain, no synthetic tiles and no broad pregeneration job.
OMNI.config.viewDistance = 0.15
OMNI.config.terrainHorizonDistance = 0
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
OMNI.test.teleport(8, 115, -8)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 8) < 1 and
        math.abs(state.playerZ + 8) < 1 and state.playerY > 114
end, 45, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(90, 25)
-- Allow normal exact-chunk reception/conversion to settle before changing the radius.
OMNI.wait(20)
OMNI.test.dumpTerrain("remote-handoff-before")
OMNI.config.viewDistance = 0
-- Config application and presentation happen after this coroutine yields. Never accept counters
-- from the old eight-chunk frame as proof that the four-chunk handoff completed.
OMNI.wait(3)
local succeeded, failure = pcall(function()
    OMNI.waitUntil(function()
        return state.terrainLodRemoteTiles > 0 and
            state.terrainLodSpatialAuthoritativeTiles > 0 and
            state.terrainLodSpatialSolidPages > 0
    end, 60, 0.1)
end)
if succeeded then OMNI.wait(3) end
OMNI.test.dumpTerrain("remote-handoff-after")
OMNI.test.dumpProfiler("remote-handoff-after")
OMNI.test.screenshot()
OMNI.wait(0.5)
assert(succeeded, "stationary remote handoff did not present: " .. tostring(failure))
assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0)
assert(math.abs(state.playerX - 8) < 1 and math.abs(state.playerZ + 8) < 1 and state.playerY > 114)
OMNI.test.pass()
