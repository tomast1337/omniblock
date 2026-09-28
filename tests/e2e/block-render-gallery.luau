-- Phase 0 characterization: every catalog block plus selected metadata variants, real chunk meshes.
-- Client-only replicas deliberately exclude simulation/placement and animated block-entity tests.
OMNI.config.viewDistance = 0
OMNI.config.terrainHorizonDistance = 0
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.bobView = false
OMNI.config.chunkFade = false
OMNI.config.cloudsQuality = 1
OMNI.config.fov = 0.15 -- close-up 43.5-degree view, all twelve pedestals remain in-frame
OMNI.config.gamma = 1
OMNI.config.alternateBlocks = false
OMNI.config.difficulty = 0
assert(OMNI.client.worlds.load("e2e-flat"))
local state = OMNI.client.state
OMNI.waitUntil(function() return state.worldLoaded and state.playerReady end, 75, 0.1)
OMNI.test.creative()
OMNI.wait(0.5)
OMNI.test.setFlying(true)
OMNI.test.setMovement(0, 0, 0)
OMNI.test.teleport(7, 109, -12)
OMNI.waitUntil(function()
    return state.playerReady and math.abs(state.playerX - 7) < 0.1 and
        math.abs(state.playerZ + 12) < 0.1 and state.playerY > 108
end, 60, 0.1)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 29)
OMNI.wait(2)
local pages = OMNI.test.blockGallery(0)
assert(pages > 0, "empty gallery")
for page = 0, pages - 1 do
    if page > 0 then assert(OMNI.test.blockGallery(page) == pages) end
    OMNI.waitUntil(OMNI.test.blockGalleryReady, 30, 0.1)
    OMNI.wait(1)
    assert(OMNI.test.blockGalleryReady(), "gallery changed after settling")
    assert(state.webGpuErrorCount == 0 and state.meshBuildFailureCount == 0, "renderer failure")
    OMNI.test.dumpTerrain("block-gallery-" .. string.format("%03d", page))
    OMNI.test.screenshot()
    OMNI.wait(1) -- completes asynchronous readback before replacing this page
end
print("block gallery captured " .. pages .. " pages")
OMNI.test.pass()
