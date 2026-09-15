-- Opt-in stable-residency reference benchmark for distances 4 and 16. Distance 32 uses the
-- fixed-warm-up view-distance-32-diagnostic: fully settling it extends the run by several minutes
-- and makes the result more dependent on world-generation throughput than renderer traversal.

OMNI.config.viewDistance = 0
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.test.setFlying(true)

local function waitForStableResidency(previousResident, timeout)
    local lastResident = -1
    local stableSamples = 0
    OMNI.waitUntil(function()
        local resident = OMNI.client.state.residentMeshCount
        local expanded = previousResident == nil or resident > previousResident
        if expanded and resident == lastResident and
            OMNI.client.state.meshPending == 0 and
            OMNI.client.state.foregroundPending == 0 and
            OMNI.client.state.backgroundPending == 0 and
            OMNI.client.state.meshSafetyHoles == 0 then
            stableSamples += 1
        else
            stableSamples = 0
        end
        lastResident = resident
        return stableSamples >= 50
    end, timeout, 0.1)
    return OMNI.client.state.residentMeshCount
end

local function sample(pitch)
    OMNI.test.setLook(0, pitch)
    OMNI.wait(1)

    local result = {
        frames = 0,
        resident = 0,
        candidates = 0,
        frustumTests = 0,
        portalVisited = 0,
        safetyRescued = 0,
        presentedSolid = 0,
        presentedTranslucent = 0,
        emptySubmitted = 0,
        draws = 0,
        uniforms = 0,
        findVisibleMs = 0,
        submitMs = 0,
        frameMs = 0
    }

    for _ = 1, 60 do
        OMNI.wait(0.05)
        result.frames += 1
        result.resident += OMNI.client.state.residentMeshCount
        result.candidates += OMNI.client.state.visibilityCandidates
        result.frustumTests += OMNI.client.state.frustumTests
        result.portalVisited += OMNI.client.state.portalVisited
        result.safetyRescued += OMNI.client.state.safetyRescued
        result.presentedSolid += OMNI.client.state.presentedSolidLayerCount
        result.presentedTranslucent += OMNI.client.state.presentedTranslucentLayerCount
        result.emptySubmitted += OMNI.client.state.emptyLayersSubmitted
        result.draws += OMNI.client.state.terrainDrawCalls
        result.uniforms += OMNI.client.state.terrainUniformEntries
        result.findVisibleMs += OMNI.client.state.findVisibleMs
        result.submitMs += OMNI.client.state.terrainSubmitCpuMs
        result.frameMs += OMNI.client.state.frameTimeMs
    end

    for key, value in pairs(result) do
        if key ~= "frames" then result[key] = value / result.frames end
    end
    return result
end

local function printSample(distance, direction, value)
    print("visibility-baseline view=" .. tostring(distance) ..
        " direction=" .. direction ..
        " resident=" .. tostring(value.resident) ..
        " candidates=" .. tostring(value.candidates) ..
        " frustumTests=" .. tostring(value.frustumTests) ..
        " portalVisited=" .. tostring(value.portalVisited) ..
        " safetyRescued=" .. tostring(value.safetyRescued) ..
        " solid=" .. tostring(value.presentedSolid) ..
        " translucent=" .. tostring(value.presentedTranslucent) ..
        " emptySubmitted=" .. tostring(value.emptySubmitted) ..
        " draws=" .. tostring(value.draws) ..
        " uniforms=" .. tostring(value.uniforms) ..
        " findVisibleMs=" .. tostring(value.findVisibleMs) ..
        " submitMs=" .. tostring(value.submitMs) ..
        " frameMs=" .. tostring(value.frameMs))
end

local distances = {
    { chunks = 4, option = 0, timeout = 60 },
    { chunks = 16, option = 12 / 28, timeout = 180 }
}

local previousResident = nil
local regressionsBefore = OMNI.client.state.presentationRegressionCount
for _, distance in ipairs(distances) do
    OMNI.config.viewDistance = distance.option
    previousResident = waitForStableResidency(previousResident, distance.timeout)

    local horizon = sample(0)
    local down = sample(90)
    assert(math.abs(horizon.resident - down.resident) < 0.01,
        "resident set changed during view " .. tostring(distance.chunks) .. " comparison")
    assert(horizon.candidates > 0 and down.candidates > 0,
        "visibility counters did not advance at view " .. tostring(distance.chunks))
    assert(horizon.frustumTests >= horizon.candidates and down.frustumTests >= down.candidates,
        "frustum accounting is incomplete at view " .. tostring(distance.chunks))
    assert(horizon.emptySubmitted == 0 and down.emptySubmitted == 0,
        "empty terrain layers were submitted at view " .. tostring(distance.chunks))
    assert(math.abs(horizon.uniforms -
            (horizon.presentedSolid + horizon.presentedTranslucent)) < 0.01 and
        math.abs(down.uniforms -
            (down.presentedSolid + down.presentedTranslucent)) < 0.01,
        "uniform/layer accounting is inconsistent at view " .. tostring(distance.chunks))

    printSample(distance.chunks, "horizon", horizon)
    printSample(distance.chunks, "down", down)
    OMNI.test.dumpTerrain("visibility-v" .. tostring(distance.chunks) .. "-down")
end

assert(OMNI.client.state.presentationRegressionCount == regressionsBefore,
    "visibility baseline observed a presentation regression")
OMNI.test.pass()
