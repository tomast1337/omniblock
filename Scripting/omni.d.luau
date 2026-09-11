-- Editor-only definitions for the client host. Do not execute or require this file.
-- Keep in sync with OmniBlock.Luau/Host and Options/GameOptions.cs.

export type OmniUiNode = {
    -- Handles can become stale when the screen changes; query again after navigation.
    parent: OmniUiNode?,
    childCount: number,
    type: string?,
    id: string?,
    text: string?,
    visible: boolean?,
    enabled: boolean?,
    hitTestVisible: boolean?,
    -- Child indices start at 1.
    child: (self: OmniUiNode, index: number) -> OmniUiNode?,
    click: (self: OmniUiNode) -> boolean,
}

export type OmniUi = {
    root: OmniUiNode?,
    hud: OmniUiNode?,
    screen: string?,
    querySelector: (selector: string) -> OmniUiNode?,
}

export type OmniWorldInfo = {
    id: string,
    name: string,
    lastPlayed: number,
    size: number,
    unsupported: boolean,
}

export type OmniClientState = {
    -- These properties are read-only at runtime.
    worldLoaded: boolean,
    playerReady: boolean,
    worldId: string?,
    meshPending: number,
    meshRequestToGpuMs: number,
}

export type OmniConfig = {
    -- Float options use their stored slider values; cycle options use zero-based indices.
    -- Assignments persist immediately. options() returns nil for non-cycle keys except language.
    options: (key: string) -> {number | string}?,
    advancedItemTooltips: boolean,
    alternateBlocks: boolean,
    anisotropicLevel: number,
    bobView: boolean,
    cameraMode: number,
    -- Release the cursor without opening a screen or pausing. Scripted camera controls still work.
    captureMouse: boolean,
    chatScale: number,
    chatWidth: number,
    chunkFade: boolean,
    cloudsQuality: number,
    controllerSensitivity: number,
    controllerType: number,
    difficulty: number,
    fov: number,
    fpsLimit: number,
    gamma: number,
    guiScale: number,
    invertYMouse: boolean,
    language: string,
    lastServer: string,
    menuMusic: boolean,
    mouseSensitivity: number,
    msaaLevel: number,
    music: number,
    -- Disable automatic pausing and extra background sleep when the window loses focus.
    pauseOnFocusLoss: boolean,
    showCoordinates: boolean,
    skin: string,
    softClouds: boolean,
    sound: number,
    uiCursors: boolean,
    useMipmaps: boolean,
    viewDistance: number,
    vsync: boolean,
    -- Access bindings with brackets, for example OMNI.config["key.forward"].
    ["key.forward"]: number,
    ["key.left"]: number,
    ["key.back"]: number,
    ["key.right"]: number,
    ["key.jump"]: number,
    ["key.sneak"]: number,
    ["key.drop"]: number,
    ["key.inventory"]: number,
    ["key.chat"]: number,
    ["key.fog"]: number,
    ["key.zoom"]: number,
    ["controller.jump"]: number,
    ["controller.inventory"]: number,
    ["controller.drop"]: number,
    ["controller.hotbarLeft"]: number,
    ["controller.hotbarRight"]: number,
    ["controller.sneak"]: number,
    ["controller.zoom"]: number,
    ["controller.pickBlock"]: number,
    ["controller.camera"]: number,
    ["controller.pause"]: number,
}

export type OmniTest = {
    pass: () -> (),
    fail: (reason: unknown?) -> (),
}

declare OMNI: {
    environment: "client",
    has: (capability: string) -> boolean,
    ui: OmniUi,
    config: OmniConfig,
    client: {
        state: OmniClientState,
        worlds: {
            list: () -> {OmniWorldInfo},
            -- Returns whether the load was queued; wait for state.playerReady afterward.
            load: (id: string) -> boolean,
        },
    },
    -- Installed only for explicit E2E launches. Assert non-nil before using in strict mode.
    test: OmniTest?,
    run: (callback: () -> ()) -> thread,
    -- Yield inside OMNI.run, or at the top level of a startup automation script.
    wait: (seconds: number) -> (),
    -- Defaults: timeoutSeconds = 5, intervalSeconds = 0. Raises an error on timeout.
    waitUntil: (predicate: () -> boolean, timeoutSeconds: number?, intervalSeconds: number?) -> boolean,
}

declare Host: {
    openScreen: (commandId: number, arg0: number, arg1: number, arg2: number) -> (),
}

declare Registry: {
    -- Registration fails once the registry phase is frozen.
    registerUi: (name: string) -> number,
}
