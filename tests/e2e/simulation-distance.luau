OMNI.config.pauseOnFocusLoss = false
OMNI.config.viewDistance = 1.0
OMNI.config.simulationDistance = 0.2

local worlds = OMNI.client.worlds.list()
assert(#worlds > 0, "fixture world is missing")
OMNI.client.worlds.load(worlds[1].id)

OMNI.waitUntil(function()
    return OMNI.client.state.playerReady
end, 60)

OMNI.waitUntil(function()
    return OMNI.client.state.renderDistance == 32 and
        OMNI.client.state.simulationDistance == 8
end, 10)

OMNI.waitUntil(function()
    return OMNI.client.state.meshSafetyHoles == 0
end, 10)

-- Runtime changes cross the integrated-server thread boundary and come back through the same
-- protocol message a remote server uses.
OMNI.config.simulationDistance = 0.0
OMNI.waitUntil(function()
    return OMNI.client.state.simulationDistance == 2
end, 10)
assert(OMNI.client.state.renderDistance == 32,
    "changing simulation distance unexpectedly changed terrain streaming distance")

OMNI.test.pass()
