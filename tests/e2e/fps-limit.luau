OMNI.config.vsync = false
OMNI.config.pauseOnFocusLoss = false
OMNI.config.captureMouse = false

-- The normalized slider's minimum is 30 FPS. Frame telemetry includes the pacer, so several
-- completed frames should settle around 33 ms without needing to enter a world.
OMNI.config.fpsLimit = 0
OMNI.wait(1)

local cappedTotal = 0
for _ = 1, 12 do
    OMNI.wait(0.05)
    cappedTotal += OMNI.client.state.frameTimeMs
end
local cappedAverage = cappedTotal / 12
print("fps limiter capped frame=" .. tostring(cappedAverage) .. "ms")
assert(cappedAverage >= 28,
    "30 FPS slider did not pace the client: " .. tostring(cappedAverage) .. "ms")

-- Maximum is the existing Unlimited sentinel. The menu-only software-rendered frame should
-- become materially shorter than the 30 FPS budget once the cap is removed.
OMNI.config.fpsLimit = 1
OMNI.wait(1)

local unlimitedTotal = 0
for _ = 1, 12 do
    OMNI.wait(0.05)
    unlimitedTotal += OMNI.client.state.frameTimeMs
end
local unlimitedAverage = unlimitedTotal / 12
print("fps limiter unlimited frame=" .. tostring(unlimitedAverage) .. "ms")
assert(unlimitedAverage < cappedAverage * 0.8,
    "Unlimited slider did not release the 30 FPS cap: capped=" .. tostring(cappedAverage) ..
    " unlimited=" .. tostring(unlimitedAverage))
OMNI.test.pass()
