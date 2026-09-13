-- A real integrated-server mob must remain presented beyond the legacy 160-block tracker cap.
-- Its source chunk remains streamed, but the mob is outside the two-chunk simulation radius, so
-- this simultaneously checks that visibility and expensive gameplay ticking are separate policies.
OMNI.config.viewDistance = 12 / 28 -- 16 chunks
OMNI.config.simulationDistance = 0 -- 2 chunks
OMNI.config.presentationQuality = 1
OMNI.config.entityImpostorDistance = 2 / 15
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

assert(OMNI.client.worlds.load("e2e-smoke"))
OMNI.waitUntil(function() return OMNI.client.state.playerReady end, 90, 0.05)
OMNI.test.creative()
OMNI.wait(0.5)

-- Spawn in the already-resident login chunk, then move the simulation center roughly 200 blocks
-- away while retaining that chunk inside the 16-chunk streamed terrain radius.
OMNI.test.setFlying(true)
local cowsBeforeSummon = OMNI.test.countEntities("omniblock:cow", 0, 16)
assert(OMNI.test.summon("cow", 1))
OMNI.waitUntil(function()
    return OMNI.test.countEntities("omniblock:cow", 0, 16) > cowsBeforeSummon
end, 20, 0.02)
OMNI.test.teleport(0, 100, -200)
OMNI.waitUntil(function() return OMNI.client.state.playerZ < -190 end, 10, 0.02)
OMNI.test.setFlying(true)
OMNI.test.setLook(0, 12)
assert(OMNI.test.countEntities("omniblock:cow", 175, 230) >= 1,
    "the distant mob was removed from the client after teleport")

OMNI.wait(10)
OMNI.test.screenshot()
local initial = OMNI.client.state
assert(initial.entityLodObserved >= 1 and
    initial.entityLodModelSubmissions + initial.entityLodImpostorSubmissions >= 1,
    "distant mob was not presented: resident=" .. tostring(initial.entityClientResident) ..
    ", presented=" .. tostring(initial.entityPresented) ..
    ", hidden=" .. tostring(initial.entityHidden) ..
    ", observed=" .. tostring(initial.entityLodObserved))
assert(initial.entityLodImpostorSubmissions >= 1,
    "server-tracked distant cow did not use its impostor: intended=" ..
    tostring(initial.entityLodIntendedImpostors) ..
    ", unsupportedState=" .. tostring(initial.entityLodUnsupportedState))

-- Distant mobs are paused server-side, not despawned. Hold long enough to catch accidental
-- tracker removal or a presentation-distance regression.
OMNI.wait(5)
assert(OMNI.client.state.entityLodObserved >= 1,
    "the distant mob stopped being presented while resident on the client")
assert(OMNI.test.countEntities("omniblock:cow", 175, 230) >= 1,
    "the passive mob despawned outside simulation distance")
assert(OMNI.client.state.webGpuErrorCount == 0)
OMNI.test.pass()
