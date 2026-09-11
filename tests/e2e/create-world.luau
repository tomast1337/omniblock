OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end
local function click(selector) assert(node(selector):click(), "could not click: " .. selector) end

assert(OMNI.ui.screen == "MainMenuScreen")
click("#main.singleplayer")
OMNI.waitUntil(function() return OMNI.ui.screen == "WorldScreen" end, 5)
click("#world.create")
OMNI.waitUntil(function() return OMNI.ui.screen == "CreateWorldScreen" end, 5)
node("#world.create.name").text = "Created E2E World"
click("#world.create.more")
node("#world.create.seed").text = "12345"
click("#world.create.submit")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 60, 0.05)
assert(OMNI.client.state.worldId == "Created E2E World")
OMNI.test.pass()
