OMNI.config.viewDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)
OMNI.waitUntil(function()
    return OMNI.client.state.meshReadyRadius >= 3 and
        OMNI.client.state.meshSafetyHoles == 0
end, 30, 0.05)

OMNI.test.creative()
OMNI.test.setFlying(true)
local regressionsBefore = OMNI.client.state.presentationRegressionCount
assert(OMNI.test.worldGenerationAuto("prepare", 16),
    "could not enable automatic generation")
OMNI.waitUntil(function()
    return OMNI.test.worldGenerationMetric("enabled") == 1 and
        OMNI.test.worldGenerationMetric("players") == 1
end, 10, 0.05)
OMNI.wait(3)
print("automatic generation diagnostics", 
    "prepared", OMNI.test.worldGenerationMetric("prepared"),
    "active", OMNI.test.worldGenerationMetric("active"),
    "throttled", OMNI.test.worldGenerationMetric("throttled"),
    "gameplayPending", OMNI.test.worldGenerationMetric("gameplayPending"),
    "backgroundQueued", OMNI.test.worldGenerationMetric("backgroundQueued"),
    "lightingPending", OMNI.test.worldGenerationMetric("lightingPending"),
    "serverTickMs", OMNI.test.worldGenerationMetric("serverTickMs"),
    "clientFrameMs", OMNI.test.worldGenerationMetric("clientFrameMs"))
OMNI.waitUntil(function()
    return OMNI.test.worldGenerationMetric("prepared") >= 1 and
        OMNI.test.worldGenerationMetric("saved") +
            OMNI.test.worldGenerationMetric("skipped") >= 1
end, 60, 0.05)

-- Crossing the radius/4 hysteresis boundary must move the preparation target without making
-- generation part of teleport or near-mesh readiness.
local preparedBeforeMove = OMNI.test.worldGenerationMetric("prepared")
local px = math.floor(OMNI.client.state.playerX)
local py = math.floor(OMNI.client.state.playerY)
local pz = math.floor(OMNI.client.state.playerZ)
OMNI.test.flyPath(px, py + 12, pz, px + 80, py + 12, pz, 5)
OMNI.waitUntil(function() return OMNI.client.state.playerX >= px + 79 end, 10, 0.05)
OMNI.waitUntil(function()
    return OMNI.test.worldGenerationMetric("prepared") > preparedBeforeMove
end, 60, 0.05)

OMNI.waitUntil(function()
    return OMNI.client.state.meshSafetyHoles == 0
end, 30, 0.05)
assert(OMNI.client.state.presentationRegressionCount == regressionsBefore,
    "automatic generation removed a resident presentation")
assert(OMNI.test.worldGenerationMetric("peakRetainedBytes") > 0,
    "automatic generation did not report bounded workspace residency")
OMNI.test.dumpTerrain("automatic-generation")

assert(OMNI.test.worldGenerationAuto("off", 0),
    "could not disable automatic generation")
OMNI.waitUntil(function()
    return OMNI.test.worldGenerationMetric("enabled") == 0
end, 10, 0.05)
OMNI.test.pass()
