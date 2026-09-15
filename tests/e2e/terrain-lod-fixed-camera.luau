-- Phase 5 fixed-camera measurement gate. The current presentation prototype supports at most a
-- 64-chunk horizon and derives LOD only from complete chunks already delivered to the client.
-- This captures the honest 64-chunk configuration; the report's coverage ratio prevents it from
-- being mistaken for a fully populated 64-chunk disk/transport fixture.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 1
OMNI.config.fogDistance = 1
OMNI.config.vsync = false
OMNI.config.fpsLimit = 1
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.test.teleport(0, 192, 320)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady and OMNI.client.state.playerY > 180 and
        OMNI.client.state.playerZ > 300
end, 45, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.setLook(180, 25)

OMNI.waitUntil(function()
    return OMNI.client.state.terrainLodResident > 0 and
        OMNI.client.state.terrainLodPresented > 0 and
        OMNI.client.state.terrainLodGpuBytes > 0
end, 30, 0.05)
OMNI.wait(2)

local fixedX = OMNI.client.state.playerX
local fixedY = OMNI.client.state.playerY
local fixedZ = OMNI.client.state.playerZ
local samples = {
    frame = {}, lodCpu = {}, nearSubmitCpu = {}, lodDraws = {}, nearDraws = {},
    resident = {}, presented = {}, gpuBytes = {}, boundaryBytes = {}, cacheBytes = {},
    workingSetBytes = {}
}

for _ = 1, 120 do
    OMNI.wait(0.05)
    table.insert(samples.frame, OMNI.client.state.frameTimeMs)
    table.insert(samples.lodCpu,
        OMNI.client.state.terrainLodSolidCpuMs + OMNI.client.state.terrainLodTranslucentCpuMs)
    table.insert(samples.nearSubmitCpu, OMNI.client.state.terrainSubmitCpuMs)
    table.insert(samples.lodDraws,
        OMNI.client.state.terrainLodPresented + OMNI.client.state.terrainLodTranslucentPresented)
    table.insert(samples.nearDraws, OMNI.client.state.terrainDrawCalls)
    table.insert(samples.resident, OMNI.client.state.terrainLodResident)
    table.insert(samples.presented, OMNI.client.state.terrainLodPresented)
    table.insert(samples.gpuBytes, OMNI.client.state.terrainLodGpuBytes)
    table.insert(samples.boundaryBytes, OMNI.client.state.terrainLodBoundaryBytes)
    table.insert(samples.cacheBytes, OMNI.client.state.terrainLodCacheBytes)
    table.insert(samples.workingSetBytes, OMNI.client.state.clientWorkingSetBytes)
end

local function distribution(values)
    table.sort(values)
    local total = 0
    for _, value in ipairs(values) do total += value end
    local function percentile(p)
        return values[math.max(1, math.ceil(#values * p))]
    end
    return { mean = total / #values, p50 = percentile(0.50), p95 = percentile(0.95) }
end

local summary = {}
for key, values in pairs(samples) do summary[key] = distribution(values) end
local requiredColumns = math.floor(math.pi * 64 * 64 + 0.5)
local coverage = summary.resident.mean / requiredColumns

assert(math.abs(OMNI.client.state.playerX - fixedX) < 0.01 and
    math.abs(OMNI.client.state.playerY - fixedY) < 0.01 and
    math.abs(OMNI.client.state.playerZ - fixedZ) < 0.01,
    "camera moved during the fixed-camera sample")
assert(summary.frame.mean > 0 and summary.lodCpu.mean > 0,
    "CPU timing counters did not advance")
assert(summary.gpuBytes.mean > 0 and summary.workingSetBytes.mean > 0,
    "memory counters did not advance")
assert(OMNI.client.state.webGpuErrorCount == 0, "WebGPU validation failed")

print("terrain-lod-fixed-camera schema=1 targetChunks=64 requiredColumns=" ..
    tostring(requiredColumns) .. " observedResident=" .. tostring(summary.resident.mean) ..
    " coverage=" .. tostring(coverage) ..
    " frameMeanMs=" .. tostring(summary.frame.mean) ..
    " frameP50Ms=" .. tostring(summary.frame.p50) ..
    " frameP95Ms=" .. tostring(summary.frame.p95) ..
    " lodCpuMeanMs=" .. tostring(summary.lodCpu.mean) ..
    " lodCpuP95Ms=" .. tostring(summary.lodCpu.p95) ..
    " nearSubmitCpuMeanMs=" .. tostring(summary.nearSubmitCpu.mean) ..
    " nearSubmitCpuP95Ms=" .. tostring(summary.nearSubmitCpu.p95) ..
    " lodDrawsMean=" .. tostring(summary.lodDraws.mean) ..
    " nearDrawsMean=" .. tostring(summary.nearDraws.mean) ..
    " lodGpuBytes=" .. tostring(summary.gpuBytes.mean) ..
    " boundaryBytes=" .. tostring(summary.boundaryBytes.mean) ..
    " cacheBytes=" .. tostring(summary.cacheBytes.mean) ..
    " workingSetBytes=" .. tostring(summary.workingSetBytes.mean) ..
    " gpuPassMs=null gpuTimingStatus=timestamp-query-feature-not-requested")

OMNI.test.dumpTerrain("lod-fixed-camera-64")
OMNI.test.screenshot()
OMNI.test.pass()
