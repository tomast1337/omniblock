local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end

local function click(selector)
    assert(node(selector):click(), "could not click UI node: " .. selector)
end

local function screen(name)
    OMNI.waitUntil(function() return OMNI.ui.screen == name end, 5)
end

screen("MainMenuScreen")
assert(node("#main").childCount >= 5, "main menu is unexpectedly small")
node("#main.singleplayer")
node("#main.multiplayer")
node("#main.language")
node("#main.options")
node("#main.quit")

click("#main.singleplayer")
screen("WorldScreen")
node("#world.list")
node("#world.select")
node("#world.create")
node("#world.rename")
node("#world.delete")
click("#world.cancel")
screen("MainMenuScreen")

OMNI.test.pass()
