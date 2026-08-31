local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end
local function click(selector) assert(node(selector):click(), "could not click: " .. selector) end
local function screen(name) OMNI.waitUntil(function() return OMNI.ui.screen == name end, 5) end
local function waitNode(selector)
    OMNI.waitUntil(function() return OMNI.ui.querySelector(selector) ~= nil end, 5)
    return node(selector)
end

screen("MainMenuScreen")
assert(node("#main.multiplayer").enabled, "multiplayer requires the E2E session token")
click("#main.multiplayer")
screen("MultiplayerScreen")
click("#multiplayer.add")
screen("EditServerScreen")
node("#server.edit.name").text = "Local E2E"
node("#server.edit.address").text = "127.0.0.1:25565"
click("#server.edit.done")
screen("MultiplayerScreen")

waitNode("#multiplayer.item.0")
click("#multiplayer.item.0")
assert(node("#multiplayer.edit").enabled)
click("#multiplayer.edit")
screen("EditServerScreen")
assert(node("#server.edit.name").text == "Local E2E")
node("#server.edit.name").text = "Edited E2E"
click("#server.edit.done")
screen("MultiplayerScreen")

waitNode("#multiplayer.item.0")
click("#multiplayer.item.0")
click("#multiplayer.delete")
screen("ConfirmationScreen")
click("#confirmation.confirm")
screen("MultiplayerScreen")
assert(OMNI.ui.querySelector("#multiplayer.item.0") == nil, "server was not deleted")
OMNI.test.pass()
