-- Second process, same disposable data/cache directory: no CPU/GPU atlas state survives launch.
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
assert(OMNI.test.entityImpostors(true, true))
OMNI.waitUntil(function() return OMNI.client.state.entityLodImpostorSubmissions == 1 end, 30, 0.01)
assert(OMNI.client.state.entityImpostorDiskHits == 1, "fresh process did not reuse the disk atlas")
assert(OMNI.client.state.entityImpostorCapturedViews == 0, "warm launch rebaked the atlas")
assert(OMNI.client.state.entityImpostorMemoryHits == 0)
assert(OMNI.client.state.entityImpostorCacheErrors == 0)
assert(OMNI.client.state.webGpuErrorCount == 0)
OMNI.test.screenshot()
OMNI.wait(1.1)
assert(OMNI.test.entityImpostors(false))
OMNI.test.clearEntityBaseline()
OMNI.test.pass()
