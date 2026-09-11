OMNI.config.viewDistance = 1.0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

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
        " pending=" .. tostring(OMNI.client.state.meshPending) ..
        " resident=" .. tostring(OMNI.client.state.residentMeshCount) ..
        " presented=" .. tostring(OMNI.client.state.presentedMeshCount) ..
        " foreground=" .. tostring(OMNI.client.state.foregroundPending) ..
        " background=" .. tostring(OMNI.client.state.backgroundPending) ..
        " leadingQueued=" .. tostring(OMNI.client.state.meshLeadingEdgeQueued) ..
        " leadingPending=" .. tostring(OMNI.client.state.meshLeadingEdgePending) ..
        " evictionGrace=" .. tostring(OMNI.client.state.meshEvictionGraceCount) ..
        " oldestForeground=" .. tostring(OMNI.client.state.oldestForegroundAge) ..
        " presentationRegressions=" .. tostring(OMNI.client.state.presentationRegressionCount))
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
OMNI.test.flyPath(movementStartX, 256, movementStartZ,
    movementStartX, 256, movementStartZ + 160, 20)
for chunk = 1, 10 do
    local distance = chunk * 16
    OMNI.waitUntil(function()
        local dx = OMNI.client.state.playerX - movementStartX
        local dz = OMNI.client.state.playerZ - movementStartZ
        return dx * dx + dz * dz >= distance * distance
    end, 10, 0.05)
    assert(OMNI.client.state.playerY > 240,
        "player lost altitude before flight chunk " .. tostring(chunk))
    assert(OMNI.client.state.meshPending <= 160,
        "mesh work escaped bounded admission at flight chunk " .. tostring(chunk) ..
        ": " .. tostring(OMNI.client.state.meshPending))
    OMNI.test.dumpTerrain("flight-chunk-" .. tostring(chunk))
    assert(OMNI.client.state.meshBuildFailureCount == 0, "mesh snapshot/build failed during flight")
    assert(OMNI.client.state.meshAwaitingUpload >= 0, "negative completed-mesh gauge")
    assert(OMNI.client.state.meshAwaitingDraw >= 0, "negative uploaded-mesh gauge")
    assert(OMNI.client.state.meshCancelledCount >= OMNI.client.state.meshSupersededCount,
        "superseded mesh count exceeds all cancellations")
    assert(OMNI.client.state.meshLeadingEdgePending <= 16,
        "leading-edge admission exceeded its two-per-worker bound")
end

OMNI.wait(0.5)
assert(OMNI.client.state.playerY > 240, "player fell while waiting for safety-ring meshes")
assert(OMNI.client.state.presentationRegressionCount == 0,
    "a previously presented near-field mesh disappeared")
OMNI.test.dumpTerrain("flight-arrival")

local destinationReady = false
for sample = 1, 6 do
    destinationReady = OMNI.client.state.meshSafetyLoadedColumns == 29 and
        OMNI.client.state.meshSafetyExpectedSections == 232 and
        OMNI.client.state.meshSafetyHoles == 0
    if destinationReady then break end
    OMNI.wait(5)
end
OMNI.test.dumpTerrain(destinationReady and "flight-recovered" or "flight-incomplete")
assert(destinationReady, "destination safety ring did not recover after cinematic flight")

print("flying streaming ready: loaded=" .. tostring(OMNI.client.state.meshSafetyLoadedColumns) ..
    " expected=" .. tostring(OMNI.client.state.meshSafetyExpectedSections) ..
    " holes=" .. tostring(OMNI.client.state.meshSafetyHoles) ..
    " distance=" .. tostring(math.sqrt(
        (OMNI.client.state.playerX - movementStartX) ^ 2 +
        (OMNI.client.state.playerZ - movementStartZ) ^ 2)))
OMNI.test.pass()
