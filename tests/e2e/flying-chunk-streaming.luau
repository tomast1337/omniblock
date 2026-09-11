OMNI.config.viewDistance = 1.0

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.teleport(160, 256, 0)

OMNI.waitUntil(function()
    return OMNI.client.state.playerX > 150 and OMNI.client.state.playerY > 240
end, 10, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 90)

OMNI.wait(0.25)
OMNI.test.dumpTerrain("initial")

OMNI.wait(2)
assert(OMNI.client.state.playerY > 240, "player left the aerial test position")
OMNI.test.dumpTerrain("after-2s")

local safetyReady = false
for sample = 1, 9 do
    OMNI.wait(5)
    print("flying stream sample " .. tostring(sample) ..
        ": loaded=" .. tostring(OMNI.client.state.meshSafetyLoadedColumns) ..
        " expected=" .. tostring(OMNI.client.state.meshSafetyExpectedSections) ..
        " holes=" .. tostring(OMNI.client.state.meshSafetyHoles) ..
        " radius=" .. tostring(OMNI.client.state.meshReadyRadius) ..
        " pending=" .. tostring(OMNI.client.state.meshPending))
    safetyReady = OMNI.client.state.meshSafetyLoadedColumns == 29 and
        OMNI.client.state.meshSafetyExpectedSections == 232 and
        OMNI.client.state.meshSafetyHoles == 0 and
        OMNI.client.state.meshReadyRadius >= 3
    if safetyReady then break end
end
OMNI.test.dumpTerrain(safetyReady and "ready" or "incomplete")
assert(safetyReady, "aerial safety ring did not become complete")

local movementStartX = OMNI.client.state.playerX
local movementStartZ = OMNI.client.state.playerZ
OMNI.test.setMovement(1, 0, 0)
OMNI.waitUntil(function()
    local dx = OMNI.client.state.playerX - movementStartX
    local dz = OMNI.client.state.playerZ - movementStartZ
    return dx * dx + dz * dz >= 4
end, 5, 0.05)
OMNI.test.setMovement(0, 0, 0)

OMNI.wait(0.5)
assert(OMNI.client.state.playerY > 240, "player fell while waiting for safety-ring meshes")
print("flying streaming ready: loaded=" .. tostring(OMNI.client.state.meshSafetyLoadedColumns) ..
    " expected=" .. tostring(OMNI.client.state.meshSafetyExpectedSections) ..
    " holes=" .. tostring(OMNI.client.state.meshSafetyHoles))
OMNI.test.pass()
