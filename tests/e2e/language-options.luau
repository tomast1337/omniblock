local function node(selector)
    local result = OMNI.ui.querySelector(selector)
    assert(result ~= nil, "missing UI node: " .. selector)
    return result
end
local function click(selector) assert(node(selector):click(), "could not click: " .. selector) end
local function screen(name) OMNI.waitUntil(function() return OMNI.ui.screen == name end, 5) end
local function findAlternativeLanguage(current)
    local function walk(item)
        if item.type == "LanguageListItem" and item.id ~= "language.item." .. current then return item end
        for index = 1, item.childCount do
            local found = walk(item:child(index))
            if found ~= nil then return found end
        end
        return nil
    end
    return walk(OMNI.ui.root)
end

screen("MainMenuScreen")
local languages = OMNI.config.options("language")
assert(#languages > 1, "expected multiple loaded languages")
click("#main.language")
screen("LanguageSelectionScreen")
node("#language.list")
local oldLanguage = OMNI.config.language
local language = findAlternativeLanguage(oldLanguage)
assert(language ~= nil, "could not find an alternative language item")
assert(language:click(), "could not select alternative language")
assert(OMNI.config.language ~= oldLanguage, "language selection did not update config")
click("#language.done")
screen("MainMenuScreen")

click("#main.options")
screen("OptionsScreen")
local oldDifficulty = OMNI.config.difficulty
click("#option.difficulty")
assert(OMNI.config.difficulty ~= oldDifficulty, "difficulty option did not cycle")
node("#options.video")
node("#options.ui")
node("#options.audio")
node("#options.controls")
click("#options.done")
screen("MainMenuScreen")
OMNI.test.pass()
