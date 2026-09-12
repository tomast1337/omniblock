-- Cold capture, memory/disk reuse, pack reload cancellation and a real changed skin dependency.
OMNI.config.viewDistance = 4 / 28
OMNI.config.simulationDistance = 0
OMNI.config.presentationQuality = 2
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.fov = 0
OMNI.config.bobView = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(0, 220, 0)
OMNI.waitUntil(function() return OMNI.client.state.playerY > 210 end, 10, 0.01)
OMNI.test.setFlying(true)
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 45, 0.05)
OMNI.test.flyPath(0, 220, 0, 0, 220, 0, 0.5)
OMNI.wait(1)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(0, 0)
assert(OMNI.test.entityBaseline("cow", 1, 32))

local function ready()
    -- Submission counters describe the last completed frame, not the current reload request.
    OMNI.wait(0.05)
    OMNI.waitUntil(function()
        assert(OMNI.client.state.webGpuErrorCount == 0, "GPU error")
        assert(OMNI.client.state.entityImpostorFailures == 0, "prototype failure")
        return OMNI.client.state.entityImpostorReady == 1 and OMNI.client.state.entityLodImpostorSubmissions == 1
    end, 30, 0.005)
end
assert(OMNI.test.entityImpostors(true, true))
ready()
OMNI.waitUntil(function() return OMNI.client.state.entityImpostorCacheWrites >= 1 end, 15, 0.01)
assert(OMNI.client.state.entityImpostorCapturedViews == 26)
local baked = OMNI.client.state.entityImpostorCapturedViews
assert(OMNI.test.entityImpostors(false))
assert(OMNI.test.entityImpostors(true, true))
OMNI.waitUntil(function() return OMNI.client.state.entityImpostorMemoryHits >= 1 end, 10, 0.005)
ready()
assert(OMNI.client.state.entityImpostorCapturedViews == baked, "memory hit rebaked")
assert(OMNI.test.impostorCache("clear-memory"))
OMNI.waitUntil(function() return OMNI.client.state.entityImpostorDiskHits >= 1 end, 10, 0.005)
ready()
assert(OMNI.client.state.entityImpostorCapturedViews == baked, "disk hit rebaked")

-- Hold a completed map so cancellation during readback is deterministic, not a timing lottery.
assert(OMNI.test.impostorCache("hold-readback"))
assert(OMNI.test.impostorCache("pack-red"))
OMNI.waitUntil(function() return OMNI.client.state.entityImpostorReadbackPending == 1 end, 30, 0.005)
ready()
OMNI.test.screenshot()
OMNI.wait(1.1)
assert(OMNI.client.state.entityImpostorCapturedViews > baked, "changed skin reused original atlas")
assert(OMNI.test.impostorCache("pack-green"))
assert(OMNI.test.impostorCache("release-readback"))
OMNI.waitUntil(function() return OMNI.client.state.entityImpostorCacheWrites >= 2 end, 30, 0.005)
ready()
OMNI.test.screenshot()
OMNI.wait(1.1)
assert(OMNI.client.state.entityImpostorCancellations >= 1, "pending readback wasn't cancelled")

-- Start another uncached red capture and invalidate it partway through.
assert(OMNI.test.impostorCache("hold-capture"))
assert(OMNI.test.impostorCache("pack-red"))
OMNI.waitUntil(function() return OMNI.client.state.entityImpostorViews > 0 and OMNI.client.state.entityImpostorViews < 26 end, 15, 0.001)
assert(OMNI.test.impostorCache("pack-original"))
assert(OMNI.test.impostorCache("release-capture"))
ready()
assert(OMNI.client.state.entityImpostorCancellations >= 2)
for i = 1, 6 do
    assert(OMNI.test.impostorCache(i % 2 == 0 and "pack-green" or "pack-red"))
    OMNI.wait(0.005)
end
assert(OMNI.test.impostorCache("pack-original"))
ready()
assert(OMNI.test.impostorCache("dispose-session"))
OMNI.waitUntil(function() return OMNI.client.state.entityImpostorDiskHits >= 2 end, 15, 0.005)
ready()
assert(OMNI.client.state.entityImpostorDiskHits >= 2)
assert(OMNI.client.state.entityImpostorCacheErrors == 0)
assert(OMNI.client.state.entityImpostorMemoryBytes <= 64 * 1024 * 1024)
assert(OMNI.client.state.webGpuErrorCount == 0)
assert(OMNI.test.entityImpostors(false))
OMNI.test.clearEntityBaseline()
OMNI.test.pass()
