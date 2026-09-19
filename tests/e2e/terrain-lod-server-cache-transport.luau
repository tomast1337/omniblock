-- Start with a deliberately small exact radius. The integrated server prepares a wider spawn
-- neighborhood, so coarse records outside the client's chunk stream must arrive through the LOD
-- request/response protocol rather than being rebuilt from client-resident chunks.
OMNI.config.viewDistance = 4 / 28
OMNI.config.terrainHorizonDistance = 1
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.cloudsQuality = 1

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

assert(type(OMNI.client.state.terrainLodRemoteRequests) == "number")
assert(type(OMNI.client.state.terrainLodRemoteTiles) == "number")
assert(type(OMNI.client.state.terrainLodRemotePending) == "number")
assert(type(OMNI.client.state.terrainLodRemoteMissing) == "number")
assert(type(OMNI.client.state.terrainLodRemoteDeferred) == "number")
assert(OMNI.client.state.terrainLodIdentityReady == 1,
    "server terrain cache identity was not negotiated")

local crossedServerCache = false
for _ = 1, 2400 do
    crossedServerCache = OMNI.client.state.terrainLodRemoteRequests > 0 and
        OMNI.client.state.terrainLodRemoteTiles > 0
    if crossedServerCache then break end
    OMNI.wait(0.05)
end

assert(crossedServerCache,
    "no terrain tile crossed the server-cache transport: requests=" ..
    tostring(OMNI.client.state.terrainLodRemoteRequests) ..
    " tiles=" .. tostring(OMNI.client.state.terrainLodRemoteTiles) ..
    " pending=" .. tostring(OMNI.client.state.terrainLodRemotePending) ..
    " missing=" .. tostring(OMNI.client.state.terrainLodRemoteMissing) ..
    " deferred=" .. tostring(OMNI.client.state.terrainLodRemoteDeferred))
assert(OMNI.client.state.webGpuErrorCount == 0,
    "WebGPU validation failed while receiving server-cache terrain")
assert(OMNI.client.state.terrainLodIdentityMismatches == 0,
    "terrain cache identity changed during transport")
assert(OMNI.client.state.terrainLodIdentityRejectedMessages == 0,
    "terrain messages arrived with a stale cache identity")

print("server-cache terrain transport: requests=" ..
    tostring(OMNI.client.state.terrainLodRemoteRequests) ..
    " tiles=" .. tostring(OMNI.client.state.terrainLodRemoteTiles) ..
    " bytes=" .. tostring(OMNI.client.state.terrainLodRemoteBytes))
OMNI.test.pass()
