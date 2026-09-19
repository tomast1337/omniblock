OMNI.config.viewDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

assert(OMNI.has("worldgen"), "world-generation API was not installed")
assert(OMNI.worldgen.available, "integrated server did not authorize world-generation control")

local centerX = math.floor(OMNI.client.state.playerX)
local centerZ = math.floor(OMNI.client.state.playerZ)
local job = OMNI.worldgen.start({
    id = "e2e_lifecycle",
    dimension = "omniblock:overworld",
    center = { x = centerX, z = centerZ },
    radiusChunks = 16,
    shape = "circle",
    profile = "play",
})

-- Start and pause deliberately share one client turn. Their FIFO server-command ordering proves
-- that the Luau facade queues mutations rather than racing the server-owned service directly.
assert(job:pause())
OMNI.waitUntil(function()
    local progress = job.progress
    return progress ~= nil and progress.status == "paused"
end, 15, 0.05)

local paused = job.progress
assert(paused.totalTargets > 1, "test job did not describe a fixed area")
assert(paused.nextTarget == 0, "paused job advanced before resume")
assert(paused.dimension == "omniblock:overworld")
assert(paused.centerChunkX == math.floor(centerX / 16))
assert(paused.centerChunkZ == math.floor(centerZ / 16))

local listed = OMNI.worldgen.list()
assert(#listed == 1 and listed[1].id == "e2e_lifecycle", "job was absent from read-only listing")
assert(not pcall(function() listed[1].status = "completed" end), "progress snapshot was mutable")
assert(not pcall(function() job.id = "changed" end), "job handle was mutable")

assert(job:resume())
OMNI.waitUntil(function()
    local progress = job.progress
    return progress ~= nil and progress.status == "running" and
        progress.preparedTargets > paused.preparedTargets
end, 30, 0.05)

local running = job.progress
assert(job:cancel())
OMNI.waitUntil(function()
    local progress = job.progress
    return progress ~= nil and progress.status == "canceled"
end, 15, 0.05)

local canceled = job.progress
assert(canceled.preparedTargets >= running.preparedTargets and
        canceled.nextTarget >= running.nextTarget,
    "cancellation regressed durable progress")
assert(string.find(canceled.throttleReason, "committed terrain retained", 1, true) ~= nil,
    "cancellation did not preserve committed terrain")
OMNI.test.pass()
