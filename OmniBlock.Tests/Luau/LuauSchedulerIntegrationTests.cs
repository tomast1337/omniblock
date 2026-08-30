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
        using LuauState state = CreateState();

        Assert.True(state.TryExecute(
            "steps = 0; OMNI.run(function() steps = 1; OMNI.wait(0.1); steps = 2 end)",
            out string scheduleError), scheduleError);
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
        using LuauState state = CreateState();
        List<string> lines = [];
        LuauLogHost.WriteLine = lines.Add;
        LuauLogHost.Install(state.Handle);

        try
        {
            Assert.True(state.TryExecute(
                "finished = false; OMNI.run(function() error(\"macro exploded\") end); " +
                "OMNI.run(function() finished = true end)", out string scheduleError), scheduleError);

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
        using LuauState state = CreateState();

        Assert.False(state.TryExecute("OMNI.wait(0.1)", out string error));
        Assert.Contains("yield", error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void InstructionBudgetAbortDoesNotDisableFutureTasks()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = CreateState();
        Assert.True(state.TryExecute("OMNI.run(function() while true do end end)", out string scheduleError), scheduleError);

        state.ResetInstructionBudget(1_000);
        LuauScheduler.Tick(state, 0.05, out _);

        state.ResetInstructionBudget(100_000);
        Assert.True(state.TryExecute("recovered = false; OMNI.run(function() recovered = true end)", out string recoveryError), recoveryError);
        Tick(state, 0.05);
        AssertValue(state, "recovered", "true");
    }

    private static LuauState CreateState()
    {
        LuauState state = new();
        state.ResetInstructionBudget(100_000);
        Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out string omniError), omniError);
        Assert.True(state.TryExecute(LuauScheduler.Bootstrap, out string schedulerError), schedulerError);
        return state;
    }

    private static void Tick(LuauState state, double seconds)
    {
        state.ResetInstructionBudget(100_000);
        Assert.True(LuauScheduler.Tick(state, seconds, out string error), error);
    }

    private static void AssertValue(LuauState state, string expression, string expected)
    {
        Assert.True(state.TryExecute(expression, out string value), value);
        Assert.Equal(expected, value);
    }
}
