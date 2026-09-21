-- Build enough aligned source coverage to produce a real level-3 parent. We lower the exact
-- distance again before travel so the retained spatial hierarchy, rather than near chunks, owns
-- the old area.
OMNI.config.viewDistance = 1.0
OMNI.config.terrainHorizonDistance = 0.2
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.cloudsQuality = 1

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

-- Parent construction can take tens of seconds on software rendering. Enter creative before that
-- wait so the fixture player cannot be killed, then pin the client flight state for both later
-- teleports. The short wait lets the integrated server apply the game-mode command first.
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)

assert(type(OMNI.client.state.terrainLodSpatialComplete) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSelected) == "number")
assert(type(OMNI.client.state.terrainLodSpatialGpuResident) == "number")
assert(type(OMNI.client.state.terrainLodSpatialHighestGpuLevel) == "number")
assert(type(OMNI.client.state.terrainLodSpatialCpuTiles) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMissingGroups) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshPending) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshQueued) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshRunning) == "number")
assert(type(OMNI.client.state.terrainLodSpatialMeshReady) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSeamDesired) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSeamGpuResident) == "number")
assert(type(OMNI.client.state.terrainLodSpatialSubmissionReady) == "number")
assert(type(OMNI.client.state.terrainLodSpatialAuthoritativeTiles) == "number")
assert(type(OMNI.client.state.terrainLodSpatialHighestAuthoritativeLevel) == "number")
assert(type(OMNI.client.state.terrainCoverageReady) == "number")
assert(type(OMNI.client.state.terrainCoverageHoles) == "number")
assert(type(OMNI.client.state.terrainCoverageOverlaps) == "number")
assert(type(OMNI.client.state.terrainCoverageMissingSeams) == "number")
assert(type(OMNI.client.state.terrainCoveragePendingSeams) == "number")
assert(type(OMNI.client.state.terrainLodCoarseSourceUnavailable) == "number")
assert(type(OMNI.client.state.terrainLodCoarseBuilding) == "number")
assert(type(OMNI.client.state.terrainLodCoarseTransportPending) == "number")
assert(type(OMNI.client.state.terrainLodCoarseGpuPending) == "number")
assert(type(OMNI.client.state.terrainLodCoarseFrontierUnknown) == "number")
assert(type(OMNI.client.state.terrainLodColdCoverMs) == "number")

local function coverageDiagnostic(stage)
    return stage .. ": covered=" .. tostring(OMNI.client.state.terrainCoverageCovered) ..
        "/" .. tostring(OMNI.client.state.terrainCoverageExpected) ..
        " holes=" .. tostring(OMNI.client.state.terrainCoverageHoles) ..
        " overlaps=" .. tostring(OMNI.client.state.terrainCoverageOverlaps) ..
        " missingSeams=" .. tostring(OMNI.client.state.terrainCoverageMissingSeams) ..
        " pendingSeams=" .. tostring(OMNI.client.state.terrainCoveragePendingSeams) ..
        " unexpectedSeams=" .. tostring(OMNI.client.state.terrainCoverageUnexpectedSeams) ..
        " failure=" .. tostring(OMNI.client.state.terrainCoverageFailureKind) ..
        "@" .. tostring(OMNI.client.state.terrainCoverageFailureX) ..
        "," .. tostring(OMNI.client.state.terrainCoverageFailureZ)
end

local function assertCoverage(stage)
    assert(OMNI.client.state.terrainCoverageExpected > 0, coverageDiagnostic(stage))
    assert(OMNI.client.state.terrainCoverageHoles == 0, coverageDiagnostic(stage))
    assert(OMNI.client.state.terrainCoverageOverlaps == 0, coverageDiagnostic(stage))
    assert(OMNI.client.state.terrainCoverageMissingSeams == 0, coverageDiagnostic(stage))
    assert(OMNI.client.state.terrainCoverageUnexpectedSeams == 0, coverageDiagnostic(stage))
    assert(OMNI.client.state.webGpuErrorCount == 0,
        stage .. ": WebGPU validation failed")
    assert(OMNI.client.state.meshBuildFailureCount == 0,
        stage .. ": mesh construction failed")
end

local function observeCoverage(seconds, stage)
    local samples = math.max(1, math.floor(seconds * 10))
    for _ = 1, samples do
        OMNI.wait(0.1)
        assertCoverage(stage)
    end
end

local stagingReady = false
-- Source loading and parent compilation share the machine with the integrated server. Allow
-- preparation time without relaxing the level-3 coverage requirement or the flight assertions.
for _ = 1, 1800 do
    stagingReady = OMNI.client.state.terrainLodSpatialCpuTiles > 0 and
        OMNI.client.state.terrainLodSpatialGpuResident > 0 and
        OMNI.client.state.terrainLodSpatialHighestGpuLevel >= 3 and
        OMNI.client.state.terrainLodSpatialComplete == 1 and
        OMNI.client.state.terrainLodSpatialSelected > 0 and
        OMNI.client.state.terrainLodSpatialSeamDesired > 0 and
        OMNI.client.state.terrainLodSpatialSeamGpuResident > 0 and
        OMNI.client.state.terrainLodSpatialSubmissionReady == 1
    if stagingReady then break end
    OMNI.wait(0.05)
end
assert(stagingReady,
    "spatial presentation staging did not become ready: cpu=" ..
    tostring(OMNI.client.state.terrainLodSpatialCpuTiles) ..
    " gpu=" .. tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " highestGpuLevel=" .. tostring(OMNI.client.state.terrainLodSpatialHighestGpuLevel) ..
    " complete=" .. tostring(OMNI.client.state.terrainLodSpatialComplete) ..
    " selected=" .. tostring(OMNI.client.state.terrainLodSpatialSelected) ..
    " seamDesired=" .. tostring(OMNI.client.state.terrainLodSpatialSeamDesired) ..
    " seamGpu=" .. tostring(OMNI.client.state.terrainLodSpatialSeamGpuResident) ..
    " seamQueued=" .. tostring(OMNI.client.state.terrainLodSpatialSeamQueued) ..
    " seamRunning=" .. tostring(OMNI.client.state.terrainLodSpatialSeamRunning) ..
    " seamReady=" .. tostring(OMNI.client.state.terrainLodSpatialSeamReady) ..
    " submission=" .. tostring(OMNI.client.state.terrainLodSpatialSubmissionReady) ..
    " authoritative=" .. tostring(OMNI.client.state.terrainLodSpatialAuthoritativeTiles))
assert(OMNI.client.state.terrainLodColdCoverMs >= 0,
    "cold coarse-cover completion time was not recorded")

-- Move far enough that the retained roots around spawn lie wholly outside the exact-render guard
-- band, then look back at them. This makes the live spatial path authoritative without ever
-- allowing a coarse tile to overlap the near renderer.
local px = OMNI.client.state.playerX
local pz = OMNI.client.state.playerZ
-- Reproduce the reported setup: four detailed chunks and a 64-chunk LOD horizon.
-- Only the initial hierarchy seeding above uses a larger detailed radius.
OMNI.config.viewDistance = 0
OMNI.test.teleport(px, 192, pz + 640)
OMNI.test.setLook(0, 25)
OMNI.waitUntil(function()
    return OMNI.client.state.playerY > 180 and OMNI.client.state.playerZ > pz + 620
end, 10, 0.05)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady
end, 45, 0.05)

local hierarchyReady = false
for _ = 1, 600 do
    hierarchyReady = OMNI.client.state.terrainLodSpatialSubmissionReady == 1 and
        OMNI.client.state.terrainLodSpatialAuthoritativeTiles > 0 and
        OMNI.client.state.terrainLodSpatialHighestAuthoritativeLevel >= 3
    if hierarchyReady then break end
    OMNI.wait(0.05)
end
assert(hierarchyReady,
    "spatial roots did not promote after moving away: submission=" ..
    tostring(OMNI.client.state.terrainLodSpatialSubmissionReady) ..
    " authoritative=" .. tostring(OMNI.client.state.terrainLodSpatialAuthoritativeTiles) ..
    " highestLevel=" .. tostring(OMNI.client.state.terrainLodSpatialHighestAuthoritativeLevel))
local promotedLevel = OMNI.client.state.terrainLodSpatialHighestAuthoritativeLevel

-- Authority is independent of the camera frustum. Return to the nearer observation point and
-- prove that a spatial page is actually selected and submitted by the terrain pass.
OMNI.test.teleport(px, 192, pz + 320)
OMNI.waitUntil(function()
    return OMNI.client.state.playerY > 180 and
        OMNI.client.state.playerZ > pz + 300 and
        OMNI.client.state.playerZ < pz + 340
end, 10, 0.05)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady
end, 45, 0.05)

local drawReady = false
for _, yaw in ipairs({0, 90, 180, 270}) do
    OMNI.test.setLook(yaw, 20)
    for _ = 1, 60 do
        drawReady = OMNI.client.state.terrainLodSpatialSubmissionReady == 1 and
            OMNI.client.state.terrainLodSpatialAuthoritativeTiles > 0 and
            OMNI.client.state.terrainLodSpatialSolidPages > 0
        if drawReady then break end
        OMNI.wait(0.05)
    end
    if drawReady then break end
end
assert(drawReady,
    "spatial roots did not submit visible pages: submission=" ..
    tostring(OMNI.client.state.terrainLodSpatialSubmissionReady) ..
    " authoritative=" .. tostring(OMNI.client.state.terrainLodSpatialAuthoritativeTiles) ..
    " solidPages=" .. tostring(OMNI.client.state.terrainLodSpatialSolidPages) ..
    " translucentPages=" .. tostring(OMNI.client.state.terrainLodSpatialTranslucentPages))
-- The retained hierarchy is north of this observation point. Capture it without the cloud layer
-- obscuring the terrain so the artifact is useful for visual-regression review as well.
OMNI.test.setLook(180, 30)
OMNI.wait(0.5)
OMNI.test.screenshot()

-- Approach the retained terrain through its near guard band. Coarse coverage must not be
-- withdrawn merely because the camera got closer; available exact columns take over while
-- unfinished columns remain covered. Exercise the masked spatial draw on the live GPU path.
OMNI.test.setLook(180, 55)
OMNI.test.flyPath(px, 192, pz + 320, px, 192, pz, 16)
for sample = 1, 16 do
    OMNI.wait(1)
    assert(OMNI.client.state.webGpuErrorCount == 0,
        "WebGPU validation failed while approaching spatial LOD")
    assert(OMNI.client.state.meshBuildFailureCount == 0,
        "mesh construction failed during the spatial-to-near handoff")
    assert(OMNI.client.state.terrainLodSpatialSubmissionReady == 1,
        "displayed spatial snapshot disappeared while replacement seams were pending")
end
OMNI.waitUntil(function()
    return math.abs(OMNI.client.state.playerX - px) < 2 and
        math.abs(OMNI.client.state.playerZ - pz) < 2 and
        OMNI.client.state.playerReady and
        OMNI.client.state.meshSafetyHoles == 0 and
        OMNI.client.state.presentedSolidLayerCount > 0
end, 30, 0.05)
assert(OMNI.client.state.playerY > 180, "player fell during LOD approach")
OMNI.test.dumpTerrain("spatial-approach")
OMNI.test.screenshot()

-- Above the world, pitch and height must not repeatedly withdraw the displayed partition.
-- Fly vertically, then rotate looking straight down at the small full-detail footprint.
OMNI.test.setLook(0, 90)
OMNI.test.flyPath(px, 192, pz, px, 512, pz, 8)
for sample = 1, 8 do
    OMNI.wait(1)
    assert(OMNI.client.state.terrainLodSpatialSubmissionReady == 1,
        "spatial publication vanished during vertical flight")
end
-- Cinematic paths advance on simulation ticks, not the script's wall clock. Never start the next
-- observation before the endpoint has actually been reached on a loaded renderer.
OMNI.waitUntil(function()
    return OMNI.client.state.playerY > 511 and OMNI.client.state.playerReady
end, 30, 0.05)
OMNI.test.dumpTerrain("spatial-high-altitude")
OMNI.test.screenshot()
OMNI.wait(0.2)
for _, yaw in ipairs({0, 90, 180, 270}) do
    OMNI.test.setLook(yaw, 90)
    -- Observe both initial synchronous culling and subsequent worker-result reuse.
    for sample = 1, 20 do
        OMNI.wait(0.05)
        assert(OMNI.client.state.terrainLodSpatialSubmissionReady == 1,
            "spatial publication vanished while looking down")
        -- At this height the full four-chunk footprint fits in view. A few emergency safety-ring
        -- rescues are not sufficient: the old worker cutoff left only about eight sections drawn.
        assert(OMNI.client.state.presentedSolidLayerCount >= 16,
            "detailed terrain below the high camera lost coverage: " ..
            tostring(OMNI.client.state.presentedSolidLayerCount) .. " solid sections")
    end
end
assert(OMNI.client.state.playerY > 500, "high-altitude observation was not reached")
OMNI.test.dumpTerrain("spatial-high-altitude")
OMNI.test.screenshot()

-- Return to the reported problem height and orbit slowly. Assertions sample throughout each
-- cinematic segment, so a one-frame owner withdrawal or camera-angle-dependent partition loss is
-- a deterministic failure rather than something an endpoint screenshot can miss.
OMNI.test.setLook(0, 70)
OMNI.test.flyPath(px, 512, pz, px + 32, 170, pz, 8)
observeCoverage(8, "descending to circular-flight fixture")
OMNI.waitUntil(function()
    return OMNI.client.state.playerY > 169 and OMNI.client.state.playerY < 172 and
        OMNI.client.state.terrainCoverageReady == 1
end, 30, 0.05)
assertCoverage("circular-flight start")

local circlePoints = 12
local radius = 32
for point = 1, circlePoints do
    local angle = point * math.pi * 2 / circlePoints
    local targetX = px + math.cos(angle) * radius
    local targetZ = pz + math.sin(angle) * radius
    OMNI.test.setLook(90 - angle * 180 / math.pi, 70)
    OMNI.test.flyPath(OMNI.client.state.playerX, 170, OMNI.client.state.playerZ,
        targetX, 170, targetZ, 2)
    observeCoverage(2, "circular flight point " .. tostring(point))
end

-- Reverse a long move before it completes. This changes the leading edge and cancels stale work
-- while the displayed partition must remain complete.
local reverseX = OMNI.client.state.playerX
local reverseZ = OMNI.client.state.playerZ
OMNI.test.flyPath(reverseX, 170, reverseZ, reverseX + 128, 170, reverseZ, 8)
observeCoverage(1, "rapid reversal outbound")
OMNI.test.flyPath(OMNI.client.state.playerX, 170, OMNI.client.state.playerZ,
    reverseX, 170, reverseZ, 2)
observeCoverage(2, "rapid reversal inbound")

-- Teleport invalidates the current streaming center. The loading contract may temporarily mark the
-- player unready, but once it releases gameplay the exact/column/spatial union must already own the
-- new footprint.
OMNI.test.teleport(px + 160, 170, pz + 160)
OMNI.waitUntil(function()
    return OMNI.client.state.playerReady and
        OMNI.client.state.playerX > px + 150 and
        OMNI.client.state.terrainCoverageReady == 1
end, 45, 0.05)
assertCoverage("post-teleport")

-- Repeated geometry changes and a cross-boundary lava update exercise replacement retention and
-- delayed neighbor evidence together. The oracle checks body ownership and both seam families on
-- every sampled frame.
local editX = math.floor(OMNI.client.state.playerX / 16) * 16 + 15
local editY = math.floor(OMNI.client.state.playerY) - 8
local editZ = math.floor(OMNI.client.state.playerZ)
for edit = 1, 24 do
    local block = edit % 2 == 0 and "omniblock:stone" or "omniblock:air"
    OMNI.test.setBlock(block, editX - (edit % 4), editY, editZ + (edit % 3))
    OMNI.wait(0.05)
end
OMNI.test.setBlock("omniblock:flowing_lava", editX, editY + 1, editZ)
OMNI.test.setBlock("omniblock:flowing_lava", editX + 1, editY + 1, editZ)
observeCoverage(5, "sustained edits and lava boundary")

assert(OMNI.client.state.webGpuErrorCount == 0,
    "WebGPU validation failed while staging spatial terrain")
print("spatial LOD live: cpu=" ..
    tostring(OMNI.client.state.terrainLodSpatialCpuTiles) ..
    " gpu=" .. tostring(OMNI.client.state.terrainLodSpatialGpuResident) ..
    " selected=" .. tostring(OMNI.client.state.terrainLodSpatialSelected) ..
    " seams=" .. tostring(OMNI.client.state.terrainLodSpatialSeamGpuResident) ..
    " live=" .. tostring(OMNI.client.state.terrainLodSpatialAuthoritativeTiles) ..
    " promotedLevel=" .. tostring(promotedLevel) ..
    " currentLevel=" .. tostring(OMNI.client.state.terrainLodSpatialHighestAuthoritativeLevel) ..
    " fallbacks=" .. tostring(OMNI.client.state.terrainLodSpatialParentFallbacks) ..
    " missing=" .. tostring(OMNI.client.state.terrainLodSpatialMissingGroups) ..
    " coverage=" .. tostring(OMNI.client.state.terrainCoverageCovered) ..
    "/" .. tostring(OMNI.client.state.terrainCoverageExpected) ..
    " holes=" .. tostring(OMNI.client.state.terrainCoverageHoles) ..
    " overlaps=" .. tostring(OMNI.client.state.terrainCoverageOverlaps) ..
    " missingSeams=" .. tostring(OMNI.client.state.terrainCoverageMissingSeams) ..
    " failure=" .. tostring(OMNI.client.state.terrainCoverageFailureKind) ..
    "@" .. tostring(OMNI.client.state.terrainCoverageFailureX) ..
    "," .. tostring(OMNI.client.state.terrainCoverageFailureZ))
OMNI.test.pass()
