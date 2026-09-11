-- Keep this test's world-streaming noise small so the counters primarily exercise the edited
-- near-field sections. The normalized option maps zero to the supported four-chunk minimum.
OMNI.config.viewDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 90)

OMNI.waitUntil(function()
    return OMNI.client.state.meshReadyRadius >= 3 and
        OMNI.client.state.meshSafetyHoles == 0
end, 30, 0.05)

local completedBefore = OMNI.client.state.meshCriticalCompletedCount
local missesBefore = OMNI.client.state.meshCriticalDeadlineMissCount
local cancelledBefore = OMNI.client.state.meshCancelledCount
local supersededBefore = OMNI.client.state.meshSupersededCount
local regressionsBefore = OMNI.client.state.presentationRegressionCount
local px = math.floor(OMNI.client.state.playerX)
local py = math.floor(OMNI.client.state.playerY)
local pz = math.floor(OMNI.client.state.playerZ)
local broken = 0
local touchedSections = {}
local touchedSectionCount = 0

local function recordDirtySections(x, y, z)
    for sx = math.floor((x - 1) / 16), math.floor((x + 1) / 16) do
        for sy = math.floor((y - 1) / 16), math.floor((y + 1) / 16) do
            for sz = math.floor((z - 1) / 16), math.floor((z + 1) / 16) do
                local key = tostring(sx) .. ":" .. tostring(sy) .. ":" .. tostring(sz)
                if not touchedSections[key] then
                    touchedSections[key] = { x = sx * 16, y = sy * 16, z = sz * 16 }
                    touchedSectionCount += 1
                end
            end
        end
    end
end

-- All edits land in the near-field section and travel through the normal player/network path.
-- Searching three layers makes the fixture robust to the exact fractional spawn height.
for dz = -2, 2 do
    for dx = -2, 2 do
        for depth = 1, 3 do
            local x = px + dx
            local y = py - depth
            local z = pz + dz
            if OMNI.test.breakBlock(x, y, z) then
                broken += 1
                recordDirtySections(x, y, z)
                break
            end
        end
    end
end

assert(broken > 0, "deadline workload found no nearby blocks to break")
local targetMissesBefore = 0
for _, section in touchedSections do
    targetMissesBefore += OMNI.test.meshDeadlineMissCount(section.x, section.y, section.z)
end
OMNI.test.dumpTerrain("critical-issued")

OMNI.waitUntil(function()
    for _, section in touchedSections do
        if not OMNI.test.isMeshCurrent(section.x, section.y, section.z) then
            return false
        end
    end
    return true
end, 15, 0.02)
OMNI.test.dumpTerrain("critical-settled")

local completedDelta = OMNI.client.state.meshCriticalCompletedCount - completedBefore
local missedDelta = OMNI.client.state.meshCriticalDeadlineMissCount - missesBefore
local targetMissesAfter = 0
for _, section in touchedSections do
    targetMissesAfter += OMNI.test.meshDeadlineMissCount(section.x, section.y, section.z)
end
assert(completedDelta > 0, "critical mesh work made no observable progress")
assert(targetMissesAfter == targetMissesBefore,
    "near-field block changes missed the critical presentation deadline")
assert(OMNI.client.state.meshCriticalDeadlineMissCount <=
    OMNI.client.state.meshCriticalCompletedCount,
    "deadline misses exceed completed critical revisions")
assert(OMNI.client.state.meshCriticalOverdueCount < 256,
    "critical deadline backlog escaped its bounded near-field workload")
assert(OMNI.client.state.meshCancelledCount >= cancelledBefore,
    "mesh cancellation counter regressed")
assert(OMNI.client.state.meshSupersededCount >= supersededBefore,
    "mesh supersession counter regressed")
assert(OMNI.client.state.meshCancelledCount >= OMNI.client.state.meshSupersededCount,
    "superseded mesh count exceeds all cancellations")
assert(OMNI.client.state.meshCooperativeCancellationCount <=
    OMNI.client.state.meshCancelledCount,
    "observed worker cancellations exceed logically cancelled requests")
assert(OMNI.client.state.presentationRegressionCount == regressionsBefore,
    "a resident near-field mesh disappeared during critical updates")
assert(OMNI.client.state.meshBuildFailureCount == 0,
    "mesh snapshot/build failed during critical updates")

print("critical mesh workload: broken=" .. tostring(broken) ..
    " sections=" .. tostring(touchedSectionCount) ..
    " completed=" .. tostring(completedDelta) ..
    " missed=" .. tostring(missedDelta) ..
    " overdue=" .. tostring(OMNI.client.state.meshCriticalOverdueCount) ..
    " cancelled=" .. tostring(OMNI.client.state.meshCancelledCount - cancelledBefore) ..
    " cooperative=" .. tostring(OMNI.client.state.meshCooperativeCancellationCount))
OMNI.test.pass()
