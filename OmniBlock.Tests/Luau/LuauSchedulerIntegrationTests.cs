using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauSchedulerIntegrationTests
{
    [SkippableFact]
    public void RunAndWaitResumeCoroutineOnSchedulerTime()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using var state = CreateState();

        Assert.True(state.TryExecute(
            "steps = 0; OMNI.run(function() steps = 1; OMNI.wait(0.1); steps = 2 end)",
            out var scheduleError), scheduleError);
        AssertValue(state, "steps", "0");

        Tick(state, 0.05);
        AssertValue(state, "steps", "1");
        Tick(state, 0.05);
        AssertValue(state, "steps", "1");
        Tick(state, 0.05);
        AssertValue(state, "steps", "2");
    }

    [SkippableFact]
    public void TaskErrorsAreLoggedWithoutStoppingOtherTasks()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using var state = CreateState();
        List<string> lines = [];
        LuauLogHost.WriteLine = lines.Add;
        LuauLogHost.Install(state.Handle);

        try
        {
            Assert.True(state.TryExecute(
                "finished = false; OMNI.run(function() error(\"macro exploded\") end); " +
                "OMNI.run(function() finished = true end)", out var scheduleError), scheduleError);

            Tick(state, 0.05);

            AssertValue(state, "finished", "true");
            Assert.Contains(lines, line => line.Contains("macro exploded", StringComparison.Ordinal));
        }
        finally
        {
            LuauLogHost.WriteLine = null;
        }
    }

    [SkippableFact]
    public void WaitCannotYieldTheConsoleMainThread()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using var state = CreateState();

        Assert.False(state.TryExecute("OMNI.wait(0.1)", out var error));
        Assert.Contains("yield", error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void InstructionBudgetAbortDoesNotDisableFutureTasks()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using var state = CreateState();
        Assert.True(state.TryExecute("OMNI.run(function() while true do end end)", out var scheduleError), scheduleError);

        state.ResetInstructionBudget(1_000);
        LuauScheduler.Tick(state, 0.05, out _);

        state.ResetInstructionBudget(100_000);
        Assert.True(state.TryExecute("recovered = false; OMNI.run(function() recovered = true end)", out var recoveryError), recoveryError);
        Tick(state, 0.05);
        AssertValue(state, "recovered", "true");
    }

    [SkippableFact]
    public void WaitUntilResumesWhenPredicateBecomesTrue()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using var state = CreateState();

        Assert.True(state.TryExecute(
            "ready = false; finished = false; " +
            "OMNI.run(function() OMNI.waitUntil(function() return ready end, 1); finished = true end)",
            out var scheduleError), scheduleError);

        Tick(state, 0.05);
        AssertValue(state, "finished", "false");
        Assert.True(state.TryExecute("ready = true", out var readyError), readyError);
        Tick(state, 0.05);
        AssertValue(state, "finished", "true");
    }

    [SkippableFact]
    public void WaitUntilTimeoutFailsOnlyItsTask()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using var state = CreateState();
        List<string> lines = [];
        LuauLogHost.WriteLine = lines.Add;
        LuauLogHost.Install(state.Handle);

        try
        {
            Assert.True(state.TryExecute(
                "OMNI.run(function() OMNI.waitUntil(function() return false end, 0.1) end)",
                out var scheduleError), scheduleError);

            Tick(state, 0.05);
            Tick(state, 0.05);
            Tick(state, 0.05);

            Assert.Contains(lines, line => line.Contains("waitUntil timed out", StringComparison.Ordinal));
        }
        finally
        {
            LuauLogHost.WriteLine = null;
        }
    }

    [SkippableFact]
    public void WaitUntilValidatesItsArguments()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using var state = CreateState();

        Assert.False(state.TryExecute("OMNI.waitUntil(true)", out var predicateError));
        Assert.Contains("predicate function", predicateError);
        Assert.False(state.TryExecute("OMNI.waitUntil(function() return false end, -1)", out var timeoutError));
        Assert.Contains("finite non-negative", timeoutError);
    }

    private static LuauState CreateState()
    {
        LuauState state = new();
        state.ResetInstructionBudget(100_000);
        Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
        Assert.True(state.TryExecute(LuauScheduler.Bootstrap, out var schedulerError), schedulerError);
        return state;
    }

    private static void Tick(LuauState state, double seconds)
    {
        state.ResetInstructionBudget(100_000);
        Assert.True(LuauScheduler.Tick(state, seconds, out var error), error);
    }

    private static void AssertValue(LuauState state, string expression, string expected)
    {
        Assert.True(state.TryExecute(expression, out var value), value);
        Assert.Equal(expected, value);
    }
}
