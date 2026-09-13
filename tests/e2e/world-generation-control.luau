-- Phase 0 control workload. No speculative/background generation service exists yet, so all
-- observed world work comes from ordinary player streaming, edits, lighting, and liquid ticks.
OMNI.config.viewDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 70)
OMNI.waitUntil(function()
    return OMNI.client.state.meshReadyRadius >= 3 and
        OMNI.client.state.meshSafetyHoles == 0
end, 30, 0.05)

local regressionsBefore = OMNI.client.state.presentationRegressionCount
local failuresBefore = OMNI.client.state.meshBuildFailureCount
local px = math.floor(OMNI.client.state.playerX)
local py = math.floor(OMNI.client.state.playerY)
local pz = math.floor(OMNI.client.state.playerZ)
OMNI.test.dumpTerrain("control-start")

-- A bounded edit burst exercises critical remeshing without depending on a particular surface
-- height. Replace a small platform in air, then let a liquid/light update run on it.
for dz = -2, 2 do
    for dx = -2, 2 do
        OMNI.test.setBlock("omniblock:stone", px + dx, py + 4, pz + dz)
    end
end
OMNI.test.setBlock("omniblock:flowing_lava", px, py + 5, pz)

-- Fixed cinematic movement produces a repeatable foreground streaming load while keeping flight
-- independent of window focus and keyboard input.
OMNI.test.flyPath(px, py + 12, pz, px + 32, py + 12, pz + 32, 8)
OMNI.waitUntil(function()
    return OMNI.client.state.playerX >= px + 31 and
        OMNI.client.state.playerZ >= pz + 31
end, 12, 0.05)
OMNI.test.dumpTerrain("control-pressure")

OMNI.waitUntil(function()
    return OMNI.client.state.meshSafetyHoles == 0 and
        OMNI.client.state.foregroundPending == 0
end, 30, 0.05)

assert(OMNI.client.state.meshBuildFailureCount == failuresBefore,
    "control workload caused a mesh build failure")
assert(OMNI.client.state.presentationRegressionCount == regressionsBefore,
    "control workload removed a resident presentation")
OMNI.test.dumpTerrain("control-settled")
OMNI.test.pass()
