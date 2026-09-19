OMNI.config.viewDistance = 1.0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

assert(type(OMNI.client.state.terrainLodPending) == "number", "LOD pending gauge is unavailable")
assert(type(OMNI.client.state.terrainLodConverting) == "number", "LOD conversion gauge is unavailable")
assert(type(OMNI.client.state.terrainLodResident) == "number", "LOD residency gauge is unavailable")
assert(type(OMNI.client.state.terrainLodLevel0Resident) == "number",
    "LOD exact-voxel residency gauge is unavailable")
assert(type(OMNI.client.state.terrainLodLevel1Resident) == "number",
    "LOD transition-level residency gauge is unavailable")
assert(type(OMNI.client.state.terrainLodPresented) == "number", "LOD presentation gauge is unavailable")
assert(type(OMNI.client.state.terrainLodTranslucentPresented) == "number",
    "LOD translucent presentation gauge is unavailable")
assert(type(OMNI.client.state.terrainLodGpuBytes) == "number", "LOD memory gauge is unavailable")
assert(type(OMNI.client.state.terrainLodMeshOwned) == "number",
    "LOD mesh ownership gauge is unavailable")
assert(type(OMNI.client.state.terrainLodMeshCoverageQueued) == "number",
    "LOD mesh coverage queue gauge is unavailable")
assert(type(OMNI.client.state.terrainLodMeshRefinementQueued) == "number",
    "LOD mesh refinement queue gauge is unavailable")
assert(type(OMNI.client.state.terrainLodMeshCompletedBytes) == "number",
    "LOD completed-result byte gauge is unavailable")
assert(type(OMNI.client.state.terrainLodMeshPredictedBytes) == "number",
    "LOD predicted-result byte gauge is unavailable")
assert(type(OMNI.client.state.terrainLodMeshPredictedMs) == "number",
    "LOD predicted compilation-time gauge is unavailable")
assert(type(OMNI.client.state.terrainLodMeshAdmissionDeferrals) == "number",
    "LOD compilation admission counter is unavailable")
assert(type(OMNI.client.state.terrainLodMeshUploadDeferrals) == "number",
    "LOD upload admission counter is unavailable")
assert(type(OMNI.client.state.terrainLodHandoffPreparing) == "number",
    "LOD handoff preparation gauge is unavailable")
assert(type(OMNI.client.state.terrainLodHandoffOverlap) == "number",
    "LOD handoff overlap gauge is unavailable")
assert(type(OMNI.client.state.terrainLodHandoffsStarted) == "number",
    "LOD handoff counter is unavailable")
assert(type(OMNI.client.state.terrainLodHandoffReversals) == "number",
    "LOD handoff reversal gauge is unavailable")
assert(type(OMNI.client.state.terrainLodLevelTransitions) == "number",
    "LOD level-transition gauge is unavailable")
assert(type(OMNI.client.state.terrainLodLevelTransitionsStarted) == "number",
    "LOD level-transition counter is unavailable")
assert(type(OMNI.client.state.terrainLodLevelTransitionReversals) == "number",
    "LOD level-transition reversal gauge is unavailable")
assert(type(OMNI.client.state.terrainLodBoundaryLinked) == "number",
    "LOD boundary-link gauge is unavailable")
assert(type(OMNI.client.state.terrainLodBoundaryPending) == "number",
    "LOD boundary-pending gauge is unavailable")
assert(type(OMNI.client.state.terrainLodBoundaryRefreshes) == "number",
    "LOD boundary-refresh counter is unavailable")
assert(type(OMNI.client.state.terrainLodBoundaryBytes) == "number",
    "LOD boundary-memory gauge is unavailable")
assert(type(OMNI.client.state.terrainLodCacheHits) == "number",
    "LOD hierarchy-cache hit counter is unavailable")
assert(type(OMNI.client.state.terrainLodCacheMisses) == "number",
    "LOD hierarchy-cache miss counter is unavailable")
assert(type(OMNI.client.state.terrainLodResourceReloads) == "number",
    "LOD resource-reload reuse counter is unavailable")
assert(type(OMNI.client.state.terrainLodResourceReusedColumns) == "number",
    "LOD resource-reload column reuse gauge is unavailable")
assert(type(OMNI.client.state.terrainLodResourceReusedGpuBytes) == "number",
    "LOD resource-reload GPU reuse gauge is unavailable")
assert(type(OMNI.client.state.terrainLodSpatialComplete) == "number",
    "spatial LOD shadow coverage gauge is unavailable")
assert(type(OMNI.client.state.terrainLodSpatialSelected) == "number",
    "spatial LOD shadow selection gauge is unavailable")
assert(type(OMNI.client.state.terrainLodSpatialGpuResident) == "number",
    "spatial LOD shadow GPU residency gauge is unavailable")
assert(type(OMNI.client.state.terrainLodSpatialCpuTiles) == "number",
    "spatial LOD shadow CPU residency gauge is unavailable")

OMNI.waitUntil(function()
    return OMNI.client.state.terrainLodResident > 0 and
        OMNI.client.state.terrainLodGpuBytes > 0
end, 20, 0.05)

local residentBeforeEdit = OMNI.client.state.terrainLodResident
local gpuBytesBeforeDistanceChange = OMNI.client.state.terrainLodGpuBytes
OMNI.config.viewDistance = 0.2
OMNI.wait(0.5)
assert(OMNI.client.state.terrainLodResident >= residentBeforeEdit,
    "view-distance contraction discarded reusable LOD presentations")
assert(OMNI.client.state.terrainLodGpuBytes >= gpuBytesBeforeDistanceChange,
    "view-distance contraction rebuilt the LOD GPU cache")
OMNI.config.viewDistance = 1.0
OMNI.wait(0.5)

local px = math.floor(OMNI.client.state.playerX)
local py = math.floor(OMNI.client.state.playerY)
local pz = math.floor(OMNI.client.state.playerZ)
OMNI.test.creative()
OMNI.wait(0.25)
OMNI.test.setBlock("omniblock:stone", px + 2, py + 3, pz)
-- The fixture spawn varies across a dry/shore boundary. Seed one deterministic translucent block
-- so this scenario tests the blended LOD path instead of depending on which spawn was selected.
OMNI.test.setBlock("omniblock:water", px + 3, py + 3, pz)
OMNI.wait(1)

-- Atomic rebuild means the old coverage is retained while the edited revision converts.
assert(OMNI.client.state.terrainLodResident >= residentBeforeEdit,
    "terrain edit removed resident LOD coverage before replacement")
assert(OMNI.client.state.terrainLodStaleResults >= 0, "LOD stale-result gauge is invalid")
assert(OMNI.client.state.terrainLodRejected >= 0, "LOD admission gauge is invalid")
assert(OMNI.client.state.terrainLodEvictions >= 0, "LOD eviction gauge is invalid")

-- Move beyond the full-detail radius and look back. The old columns should have been evicted from
-- the near renderer while their independently owned LOD presentations remain drawable.
OMNI.test.teleport(px, 192, pz + 320)
OMNI.waitUntil(function()
    return OMNI.client.state.playerY > 180 and OMNI.client.state.playerZ > pz + 300
end, 10, 0.05)
OMNI.test.setFlying(true)
OMNI.test.setLook(180, 25)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady
end, 45, 0.05)
local presentationReady = false
for _ = 1, 400 do
    presentationReady = OMNI.client.state.terrainLodLevel0Resident > 0 and
        OMNI.client.state.terrainLodLevel1Resident > 0 and
        OMNI.client.state.terrainLodPresented > 0 and
        OMNI.client.state.terrainLodTranslucentPresented > 0
    if presentationReady then break end
    OMNI.wait(0.05)
end
assert(presentationReady,
    "LOD presentation did not become ready: level0=" ..
    tostring(OMNI.client.state.terrainLodLevel0Resident) ..
    " level1=" .. tostring(OMNI.client.state.terrainLodLevel1Resident) ..
    " handoffs=" .. tostring(OMNI.client.state.terrainLodHandoffsStarted) ..
    " solid=" .. tostring(OMNI.client.state.terrainLodPresented) ..
    " translucent=" .. tostring(OMNI.client.state.terrainLodTranslucentPresented))
-- Let hierarchy handoffs and neighbor-boundary refreshes advance beyond their 250 ms overlap.
OMNI.wait(1.5)
assert(OMNI.client.state.terrainLodBoundaryLinked > 0,
    "resident LOD columns did not establish any neighbor seam evidence")
assert(OMNI.client.state.terrainLodBoundaryBytes > 0,
    "resident LOD boundary summaries report no memory")
assert(OMNI.client.state.terrainLodMeshCompilationSamples > 0,
    "LOD compilation cost model received no observations")
assert(OMNI.client.state.terrainLodMeshUploadSamples > 0,
    "LOD upload cost model received no observations")
assert(OMNI.client.state.terrainLodMeshCompletedBytes >= 0 and
       OMNI.client.state.terrainLodMeshPredictedBytes >= 0 and
       OMNI.client.state.terrainLodMeshPredictedMs >= 0,
    "LOD mesh backpressure gauges became negative")

-- Terrain vertices address stable named-atlas layers, so a pack reload replaces texture pixels
-- without invalidating hierarchy data or otherwise compatible GPU meshes. Pack-dependent entity
-- impostors use the same restricted reload hook but own a separate generation invalidation.
local residentBeforeReload = OMNI.client.state.terrainLodResident
local gpuBytesBeforeReload = OMNI.client.state.terrainLodGpuBytes
local reloadsBefore = OMNI.client.state.terrainLodResourceReloads
assert(OMNI.test.impostorCache("reload"), "could not reload texture resources")
OMNI.waitUntil(function()
    return OMNI.client.state.terrainLodResourceReloads > reloadsBefore
end, 10, 0.05)
assert(OMNI.client.state.terrainLodResourceReusedColumns >= residentBeforeReload,
    "resource reload did not retain the resident LOD columns")
assert(OMNI.client.state.terrainLodResourceReusedGpuBytes >= gpuBytesBeforeReload,
    "resource reload did not retain compatible LOD GPU geometry")
OMNI.test.screenshot()

print("terrain LOD: resident=" .. tostring(OMNI.client.state.terrainLodResident) ..
    " level0=" .. tostring(OMNI.client.state.terrainLodLevel0Resident) ..
    " level1=" .. tostring(OMNI.client.state.terrainLodLevel1Resident) ..
    " presented=" .. tostring(OMNI.client.state.terrainLodPresented) ..
    " translucent=" .. tostring(OMNI.client.state.terrainLodTranslucentPresented) ..
    " handoffs=" .. tostring(OMNI.client.state.terrainLodHandoffsStarted) ..
    " overlap=" .. tostring(OMNI.client.state.terrainLodHandoffOverlap) ..
    " levelTransitions=" .. tostring(OMNI.client.state.terrainLodLevelTransitionsStarted) ..
    " levelReversals=" .. tostring(OMNI.client.state.terrainLodLevelTransitionReversals) ..
    " boundaryLinked=" .. tostring(OMNI.client.state.terrainLodBoundaryLinked) ..
    " boundaryPending=" .. tostring(OMNI.client.state.terrainLodBoundaryPending) ..
    " boundaryRefreshes=" .. tostring(OMNI.client.state.terrainLodBoundaryRefreshes) ..
    " boundaryBytes=" .. tostring(OMNI.client.state.terrainLodBoundaryBytes) ..
    " gpuBytes=" .. tostring(OMNI.client.state.terrainLodGpuBytes) ..
    " meshOwned=" .. tostring(OMNI.client.state.terrainLodMeshOwned) ..
    " meshDoneBytes=" .. tostring(OMNI.client.state.terrainLodMeshCompletedBytes) ..
    " meshPredictedBytes=" .. tostring(OMNI.client.state.terrainLodMeshPredictedBytes) ..
    " meshPredictedMs=" .. tostring(OMNI.client.state.terrainLodMeshPredictedMs) ..
    " meshCompileWaits=" .. tostring(OMNI.client.state.terrainLodMeshAdmissionDeferrals) ..
    " meshUploadWaits=" .. tostring(OMNI.client.state.terrainLodMeshUploadDeferrals) ..
    " spatialComplete=" .. tostring(OMNI.client.state.terrainLodSpatialComplete) ..
    " spatialSelected=" .. tostring(OMNI.client.state.terrainLodSpatialSelected) ..
    " spatialGpu=" .. tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " spatialCpu=" .. tostring(OMNI.client.state.terrainLodSpatialCpuTiles) ..
    " pending=" .. tostring(OMNI.client.state.terrainLodPending) ..
    " converting=" .. tostring(OMNI.client.state.terrainLodConverting))
OMNI.test.pass()
