-- Keep the CI guard small enough to reach a stable resident set. The opt-in visibility baseline
-- covers 4/16/32; this test compares camera directions without streaming changing the world under
-- the two samples.
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

OMNI.waitUntil(function()
    return OMNI.client.state.meshSafetyHoles == 0 and
        OMNI.client.state.meshReadyRadius >= 3
end, 45, 0.05)

local stableResident = -1
local stableSamples = 0
OMNI.waitUntil(function()
    local resident = OMNI.client.state.residentMeshCount
    if resident == stableResident and OMNI.client.state.meshPending == 0 then
        stableSamples += 1
    else
        stableResident = resident
        stableSamples = 0
    end
    return stableSamples >= 10
end, 45, 0.1)

local playerX = OMNI.client.state.playerX
local playerY = OMNI.client.state.playerY
local playerZ = OMNI.client.state.playerZ
local regressionsBefore = OMNI.client.state.presentationRegressionCount

local function sampleDirection(pitch)
    OMNI.test.setLook(0, pitch)
    OMNI.wait(1)

    local presented = 0
    local resident = 0
    local solidDraws = 0
    local translucentDraws = 0
    local frameTime = 0
    local visibilityCandidates = 0
    local frustumTests = 0
    local portalVisited = 0
    local safetyRescued = 0
    local residentSolid = 0
    local residentTranslucent = 0
    local presentedSolid = 0
    local presentedTranslucent = 0
    local emptySubmitted = 0
    local terrainDraws = 0
    local terrainUniforms = 0
    local findVisibleMs = 0
    local terrainSubmitCpuMs = 0
    local samples = 40
    for _ = 1, samples do
        OMNI.wait(0.05)
        presented += OMNI.client.state.presentedMeshCount
        resident += OMNI.client.state.residentMeshCount
        solidDraws += OMNI.client.state.solidDrawsLastFrame
        translucentDraws += OMNI.client.state.translucentDrawsLastFrame
        frameTime += OMNI.client.state.frameTimeMs
        visibilityCandidates += OMNI.client.state.visibilityCandidates
        frustumTests += OMNI.client.state.frustumTests
        portalVisited += OMNI.client.state.portalVisited
        safetyRescued += OMNI.client.state.safetyRescued
        residentSolid += OMNI.client.state.residentSolidLayerCount
        residentTranslucent += OMNI.client.state.residentTranslucentLayerCount
        presentedSolid += OMNI.client.state.presentedSolidLayerCount
        presentedTranslucent += OMNI.client.state.presentedTranslucentLayerCount
        emptySubmitted += OMNI.client.state.emptyLayersSubmitted
        terrainDraws += OMNI.client.state.terrainDrawCalls
        terrainUniforms += OMNI.client.state.terrainUniformEntries
        findVisibleMs += OMNI.client.state.findVisibleMs
        terrainSubmitCpuMs += OMNI.client.state.terrainSubmitCpuMs
    end

    return {
        presented = presented / samples,
        resident = resident / samples,
        solidDraws = solidDraws / samples,
        translucentDraws = translucentDraws / samples,
        frameTime = frameTime / samples,
        visibilityCandidates = visibilityCandidates / samples,
        frustumTests = frustumTests / samples,
        portalVisited = portalVisited / samples,
        safetyRescued = safetyRescued / samples,
        residentSolid = residentSolid / samples,
        residentTranslucent = residentTranslucent / samples,
        presentedSolid = presentedSolid / samples,
        presentedTranslucent = presentedTranslucent / samples,
        emptySubmitted = emptySubmitted / samples,
        terrainDraws = terrainDraws / samples,
        terrainUniforms = terrainUniforms / samples,
        findVisibleMs = findVisibleMs / samples,
        terrainSubmitCpuMs = terrainSubmitCpuMs / samples
    }
end

local horizon = sampleDirection(0)
local down = sampleDirection(90)

print("frustum horizon presented=" .. tostring(horizon.presented) ..
    " solid=" .. tostring(horizon.solidDraws) ..
    " translucent=" .. tostring(horizon.translucentDraws) ..
    " frame=" .. tostring(horizon.frameTime) .. "ms")
print("frustum horizon candidates=" .. tostring(horizon.visibilityCandidates) ..
    " tests=" .. tostring(horizon.frustumTests) ..
    " portal=" .. tostring(horizon.portalVisited) ..
    " rescue=" .. tostring(horizon.safetyRescued) ..
    " layers=" .. tostring(horizon.presentedSolid) .. "/" .. tostring(horizon.presentedTranslucent) ..
    " residentLayers=" .. tostring(horizon.residentSolid) .. "/" .. tostring(horizon.residentTranslucent) ..
    " empty=" .. tostring(horizon.emptySubmitted) ..
    " draws=" .. tostring(horizon.terrainDraws) ..
    " uniforms=" .. tostring(horizon.terrainUniforms) ..
    " findVisible=" .. tostring(horizon.findVisibleMs) .. "ms" ..
    " submit=" .. tostring(horizon.terrainSubmitCpuMs) .. "ms")
print("frustum down presented=" .. tostring(down.presented) ..
    " solid=" .. tostring(down.solidDraws) ..
    " translucent=" .. tostring(down.translucentDraws) ..
    " frame=" .. tostring(down.frameTime) .. "ms")
print("frustum down candidates=" .. tostring(down.visibilityCandidates) ..
    " tests=" .. tostring(down.frustumTests) ..
    " portal=" .. tostring(down.portalVisited) ..
    " rescue=" .. tostring(down.safetyRescued) ..
    " layers=" .. tostring(down.presentedSolid) .. "/" .. tostring(down.presentedTranslucent) ..
    " residentLayers=" .. tostring(down.residentSolid) .. "/" .. tostring(down.residentTranslucent) ..
    " empty=" .. tostring(down.emptySubmitted) ..
    " draws=" .. tostring(down.terrainDraws) ..
    " uniforms=" .. tostring(down.terrainUniforms) ..
    " findVisible=" .. tostring(down.findVisibleMs) .. "ms" ..
    " submit=" .. tostring(down.terrainSubmitCpuMs) .. "ms")

OMNI.test.dumpTerrain("frustum-down")
-- Presented-section count includes the conservative safety-ring rescue and therefore is not
-- expected to be monotonic with GPU cost yet. This guard proves camera direction reaches terrain
-- selection; the printed frame/draw values are the benchmark used to guide the spatial-culler
-- follow-up without imposing machine-specific timing thresholds in CI.
local presentedDelta = math.abs(down.presented - horizon.presented)
assert(presentedDelta >= math.max(1, horizon.presented * 0.05),
    "camera direction did not materially change terrain selection: horizon=" ..
    tostring(horizon.presented) .. " down=" .. tostring(down.presented))
assert(horizon.frameTime > 0 and down.frameTime > 0, "directional frame timing did not advance")
for name, sample in pairs({ horizon = horizon, down = down }) do
    assert(sample.visibilityCandidates > 0, name .. " visibility candidates did not advance")
    assert(sample.frustumTests >= sample.visibilityCandidates,
        name .. " recorded fewer frustum tests than resident candidates")
    assert(sample.presentedSolid <= sample.residentSolid,
        name .. " presented more solid layers than are resident")
    assert(sample.presentedTranslucent <= sample.residentTranslucent,
        name .. " presented more translucent layers than are resident")
    assert(sample.emptySubmitted == 0,
        name .. " submitted empty terrain layers: " .. tostring(sample.emptySubmitted))
    assert(math.abs(sample.terrainUniforms -
            (sample.presentedSolid + sample.presentedTranslucent)) < 0.01,
        name .. " uniform entries do not match presented layers: uniforms=" ..
        tostring(sample.terrainUniforms) .. " layers=" ..
        tostring(sample.presentedSolid + sample.presentedTranslucent))
    assert(sample.findVisibleMs > 0 and sample.terrainSubmitCpuMs > 0,
        name .. " presentation timing did not advance")
end
assert(math.abs(OMNI.client.state.playerX - playerX) < 0.01 and
    math.abs(OMNI.client.state.playerY - playerY) < 0.01 and
    math.abs(OMNI.client.state.playerZ - playerZ) < 0.01,
    "player moved while comparing camera directions")
assert(math.abs(horizon.resident - down.resident) < 0.01,
    "resident mesh set changed while comparing camera directions")
assert(OMNI.client.state.presentationRegressionCount == regressionsBefore,
    "directional baseline observed a presentation regression")
OMNI.test.pass()
