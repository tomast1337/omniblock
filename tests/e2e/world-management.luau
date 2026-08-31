local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end
local function click(selector) assert(node(selector):click(), "could not click: " .. selector) end
local function screen(name)
    OMNI.waitUntil(function() return OMNI.ui.screen == name end, 5)
end
local function waitNode(selector)
    OMNI.waitUntil(function() return OMNI.ui.querySelector(selector) ~= nil end, 5)
    return node(selector)
end

screen("MainMenuScreen")
click("#main.singleplayer")
screen("WorldScreen")
waitNode("#world.item.e2e-smoke")

waitNode("#world.item.e2e-smoke")
click("#world.item.e2e-smoke")
assert(node("#world.rename").enabled)
click("#world.rename")
screen("RenameWorldScreen")
node("#world.rename.name").text = "Renamed E2E World"
click("#world.rename.submit")
screen("WorldScreen")

click("#world.item.e2e-smoke")
click("#world.delete")
screen("ConfirmationScreen")
click("#confirmation.confirm")
screen("WorldScreen")
assert(OMNI.ui.querySelector("#world.item.e2e-smoke") == nil, "fixture world was not deleted")

click("#world.create")
screen("CreateWorldScreen")
node("#world.create.name").text = "Automated World"
click("#world.create.more")
node("#world.create.seed").text = "12345"
click("#world.create.more")
assert(node("#world.create.name").text == "Automated World", "world form lost its state")
click("#world.create.cancel")
screen("WorldScreen")
OMNI.test.pass()
