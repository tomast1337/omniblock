OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false
OMNI.config.presentationQuality = 0
assert(OMNI.config.presentationQuality == 0,
    "non-terrain presentation quality was not scriptable")

assert(OMNI.ui.screen == "MainMenuScreen", "debug client did not reach the main menu")
assert(OMNI.client.state.debugOpen, "--debug did not open the debug dashboard")

assert(OMNI.client.worlds.load("e2e-smoke"), "could not launch fixture world with --debug")
OMNI.waitUntil(function()
    return OMNI.client.state.worldLoaded and OMNI.client.state.playerReady
end, 75, 0.05)

OMNI.waitUntil(function()
    return OMNI.client.state.frameTimeMs > 0 and
        OMNI.client.state.residentMeshCount > 0
end, 15, 0.05)

OMNI.wait(2)
assert(OMNI.client.state.debugOpen, "debug dashboard closed while entering the world")
assert(OMNI.client.state.playerReady, "game stopped while the debug dashboard was open")
assert(OMNI.client.state.meshBuildFailureCount == 0,
    "mesh build failed while the debug dashboard was open")
OMNI.test.dumpTerrain("debug-open")
OMNI.test.pass()
