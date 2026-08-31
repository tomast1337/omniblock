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
OMNI.test.pass()
