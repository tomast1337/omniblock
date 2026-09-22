-- Visual characterization, not a visual-quality pass. No synthetic LOD tiles or far-area job.
OMNI.config.viewDistance = 0.15 -- eight exact chunks for the reference
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
assert(OMNI.client.worlds.load("e2e-smoke"))
local state = OMNI.client.state
local function waitFor(predicate, seconds)
    local ok, failure = pcall(function() OMNI.waitUntil(predicate, seconds, 0.1) end)
    if not ok then
        OMNI.test.dumpTerrain("near-quality-wait-failed")
        error(tostring(failure) .. " at player=" .. state.playerX .. "," .. state.playerY .. "," .. state.playerZ)
    end
end
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
-- Spawn placement can vary between runs. Keep the actual comparison coordinates fixed.
local x, z = 8, -8
OMNI.test.teleport(x, 115, z)
-- Server teleport coordinates are feet positions; client Y includes its stance offset.
waitFor(function()
    return state.playerReady and state.playerY > 114 and state.playerY < 118 and
        math.abs(state.playerX - x) < 1 and math.abs(state.playerZ - z) < 1
end, 45)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 18)

-- Controlled shapes over regular generated terrain, five chunks ahead: one-block steps,
-- an open arch, a snow cap, supported foliage and a contained water surface. The open arch is
-- a vertical-gap tripwire, not a substitute for the later natural-cave visual fixture.
local targets = {}
local function block(id, dx, y, dz)
    OMNI.test.setBlock("omniblock:" .. id, x + dx, y, z + dz)
    table.insert(targets, {id = "omniblock:" .. id, x = x + dx, y = y, z = z + dz})
end
for dx = -12, -5 do block("stone", dx, 90 + (dx + 12) % 4, 80) end
for y = 90, 94 do
    block("stone", -3, y, 80)
    block("stone", 3, y, 80)
end
for dx = -3, 3 do block("stone", dx, 95, 80) end
block("snow", 0, 96, 80)
for y = 90, 93 do block("log", -17, y, 80) end
for dx = -18, -16 do block("leaves", dx, 94, 80) end
for dx = 8, 12 do
    for dz = 78, 82 do
        block("stone", dx, 90, dz)
        if dx == 8 or dx == 12 or dz == 78 or dz == 82 then
            block("stone", dx, 91, dz)
        else
            block("water", dx, 91, dz)
        end
    end
end
waitFor(function()
    for _, target in ipairs(targets) do
        if not OMNI.test.hasBlock(target.id, target.x, target.y, target.z) then return false end
        if not OMNI.test.isMeshCurrent(target.x, target.y, target.z) then return false end
    end
    return true
end, 40)

local function capture(label)
    OMNI.wait(3)
    assert(state.playerY > 114, "camera fell")
    assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0, "render failure")
    OMNI.test.dumpTerrain(label)
    OMNI.test.dumpProfiler(label)
    print("near-quality capture=" .. label .. " camera=" .. state.playerX .. "," .. state.playerY .. "," .. state.playerZ ..
        " landmark=" .. x .. ",95," .. (z + 80))
    OMNI.test.screenshot()
    OMNI.wait(0.5) -- capture completes before changing view distance
end

capture("near-quality-exact-reference")
OMNI.config.viewDistance = 0 -- same camera, four exact chunks; landmark now belongs to LOD
waitFor(function()
    return state.playerReady and state.terrainLodSpatialSubmissionReady == 1 and
        state.terrainLodSpatialGpuResident > 0
end, 60)
capture("near-quality-lod-initial")
OMNI.wait(12)
-- A later time sample, not an assertion that source coverage or visual quality has converged.
capture("near-quality-lod-settled")
OMNI.test.setLook(0, 55)
capture("near-quality-downward")
OMNI.test.pass()
