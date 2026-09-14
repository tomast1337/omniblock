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
OMNI.waitUntil(function()
    return OMNI.client.state.terrainLodLevel0Resident > 0 and
        OMNI.client.state.terrainLodLevel1Resident > 0 and
        OMNI.client.state.terrainLodHandoffsStarted > 0 and
        OMNI.client.state.terrainLodPresented > 0 and
    OMNI.client.state.terrainLodTranslucentPresented > 0
end, 20, 0.05)
OMNI.wait(0.25)
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
    " gpuBytes=" .. tostring(OMNI.client.state.terrainLodGpuBytes) ..
    " pending=" .. tostring(OMNI.client.state.terrainLodPending) ..
    " converting=" .. tostring(OMNI.client.state.terrainLodConverting))
OMNI.test.pass()
