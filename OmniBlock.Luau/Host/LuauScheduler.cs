namespace OmniBlock.Luau.Host;

/// <summary>Cooperative, client-tick-driven scheduler for persistent Luau macro tasks.</summary>
public static class LuauScheduler
{
    public static bool Tick(LuauState state, double deltaSeconds, out string error) =>
        state.TryCallGlobal("__OmniSchedulerTick", deltaSeconds, out error);

    public const string Bootstrap = """
local scheduledTasks = {}
local schedulerClock = 0

local function schedulerTick(deltaSeconds)
    schedulerClock += math.max(deltaSeconds, 0)

    -- Walk backwards over the starting task count. Tasks created by a running task begin on the
    -- following tick, and removals cannot shift an unvisited task out from under the loop.
    for index = #scheduledTasks, 1, -1 do
        local task = scheduledTasks[index]
        if task.wake <= schedulerClock then
            local ok, delay = coroutine.resume(task.thread)
            if not ok then
                print("scheduled Luau task failed: " .. tostring(delay))
                table.remove(scheduledTasks, index)
            elseif coroutine.status(task.thread) == "dead" then
                table.remove(scheduledTasks, index)
            elseif type(delay) ~= "number" or delay ~= delay or delay < 0 or delay == math.huge then
                print("scheduled Luau task failed: OMNI.wait requires a finite non-negative number")
                table.remove(scheduledTasks, index)
            else
                task.wake = schedulerClock + delay
            end
        end
    end
end

function OMNI.wait(seconds)
    if type(seconds) ~= "number" or seconds ~= seconds or seconds < 0 or seconds == math.huge then
        error("OMNI.wait requires a finite non-negative number", 2)
    end
    return coroutine.yield(seconds)
end

function OMNI.waitUntil(predicate, timeoutSeconds, intervalSeconds)
    if type(predicate) ~= "function" then
        error("OMNI.waitUntil requires a predicate function", 2)
    end

    if timeoutSeconds == nil then timeoutSeconds = 5 end
    if intervalSeconds == nil then intervalSeconds = 0 end

    if type(timeoutSeconds) ~= "number" or timeoutSeconds ~= timeoutSeconds or timeoutSeconds < 0 or timeoutSeconds == math.huge then
        error("OMNI.waitUntil timeout requires a finite non-negative number", 2)
    end
    if type(intervalSeconds) ~= "number" or intervalSeconds ~= intervalSeconds or intervalSeconds < 0 or intervalSeconds == math.huge then
        error("OMNI.waitUntil interval requires a finite non-negative number", 2)
    end

    local deadline = schedulerClock + timeoutSeconds
    while true do
        if predicate() then return true end
        if schedulerClock >= deadline then
            error("OMNI.waitUntil timed out after " .. tostring(timeoutSeconds) .. " seconds", 2)
        end
        OMNI.wait(math.min(intervalSeconds, deadline - schedulerClock))
    end
end

function OMNI.run(callback)
    if type(callback) ~= "function" then error("OMNI.run requires a function", 2) end
    local thread = coroutine.create(callback)
    table.insert(scheduledTasks, { thread = thread, wake = schedulerClock })
    return thread
end

__OmniSchedulerTick = schedulerTick
""";
}
