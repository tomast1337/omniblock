OMNI.config.viewDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.test.creative()
OMNI.wait(0.5)

local targetX = math.floor(OMNI.client.state.playerX) + 160
local targetZ = math.floor(OMNI.client.state.playerZ) + 160
OMNI.test.teleport(targetX, 96, targetZ)

OMNI.waitUntil(function()
    return math.abs(OMNI.client.state.playerX - targetX) < 2 and
        math.abs(OMNI.client.state.playerZ - targetZ) < 2
end, 10, 0.05)

assert(not OMNI.client.state.playerReady,
    "multi-chunk teleport entered gameplay before destination preload")
OMNI.test.dumpTerrain("teleport-preloading")

OMNI.waitUntil(function()
    return OMNI.client.state.playerReady
end, 45, 0.05)

assert(math.abs(OMNI.client.state.playerX - targetX) < 2,
    "player moved away from teleport destination during preload")
assert(math.abs(OMNI.client.state.playerZ - targetZ) < 2,
    "player moved away from teleport destination during preload")
assert(OMNI.client.state.residentMeshCount > 0,
    "teleport completed without resident destination meshes")
assert(OMNI.client.state.meshBuildFailureCount == 0,
    "destination mesh build failed during teleport preload")

OMNI.test.dumpTerrain("teleport-ready")
OMNI.test.pass()
