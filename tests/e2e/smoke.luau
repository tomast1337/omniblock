OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end

local function click(selector)
    assert(node(selector):click(), "could not click UI node: " .. selector)
end

assert(OMNI.ui.screen == "MainMenuScreen", "client did not start on the main menu")

click("#main.singleplayer")
OMNI.waitUntil(function()
    return OMNI.ui.screen == "WorldScreen"
end, 5)

OMNI.waitUntil(function()
    return OMNI.ui.querySelector("#world.item.e2e-smoke") ~= nil
end, 5)

click("#world.item.e2e-smoke")
local selectWorld = node("#world.select")
assert(selectWorld.enabled, "fixture world is not selectable")
assert(selectWorld:click(), "could not launch the fixture world")

OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 60, 0.05)

assert(OMNI.client.state.worldId == "e2e-smoke", "the wrong world was loaded")
assert(type(OMNI.client.state.meshPending) == "number", "mesh backlog is unavailable")
assert(OMNI.client.state.meshPending >= 0, "mesh backlog cannot be negative")
assert(type(OMNI.client.state.meshCooperativeCancellationCount) == "number",
    "cooperative mesh cancellation count is unavailable")
assert(type(OMNI.client.state.meshCriticalCompletedCount) == "number",
    "critical mesh completion count is unavailable")
assert(type(OMNI.client.state.meshCriticalDeadlineMissCount) == "number",
    "critical mesh deadline miss count is unavailable")
assert(type(OMNI.client.state.meshCriticalOverdueCount) == "number",
    "critical overdue mesh count is unavailable")
assert(type(OMNI.client.state.meshRequestToGpuMs) == "number", "mesh latency is unavailable")
assert(OMNI.client.state.meshRequestToGpuMs >= 0, "mesh latency cannot be negative")
-- Longer than the automatic focus-loss pause delay; also exercised with the window unfocused.
OMNI.wait(1)
assert(OMNI.client.state.playerReady, "gameplay paused after entry with background running enabled")
OMNI.test.pass()
