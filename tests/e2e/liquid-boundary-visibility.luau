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
local playerX = math.floor(OMNI.client.state.playerX)
local y = math.floor(OMNI.client.state.playerY) + 2
local z = math.floor(OMNI.client.state.playerZ)
local boundaryX = math.floor(playerX / 16) * 16 + 15

-- Repeated water and lighting updates cross the section boundary while the spatial index replaces
-- layer summaries. Empty presentations must remain connected without becoming visible holes.
OMNI.test.setBlock("omniblock:flowing_water", boundaryX, y, z)
OMNI.test.setBlock("omniblock:flowing_water", boundaryX + 1, y, z)
OMNI.wait(0.5)

for _ = 1, 80 do
    OMNI.wait(0.05)
    assert(OMNI.client.state.meshSafetyHoles == 0,
        "liquid boundary introduced a safety-ring render hole")
end

assert(OMNI.client.state.presentationRegressionCount == regressionsBefore,
    "liquid boundary caused a resident presentation regression")
assert(OMNI.client.state.meshBuildFailureCount == 0,
    "liquid boundary caused a mesh build failure")
OMNI.test.dumpTerrain("liquid-boundary")
OMNI.test.pass()
