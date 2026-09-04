using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauTestHostIntegrationTests
{
    [SkippableFact]
    public void BootstrapExposesPassFailAndTestCapabilityOnlyWhenInstalled()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        var passes = 0;
        string? failure = null;
        LuauTestHost.Pass = () => passes++;
        LuauTestHost.Fail = reason => failure = reason;

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            Assert.True(state.TryExecute("OMNI.test == nil", out var initiallyMissing), initiallyMissing);
            Assert.Equal("true", initiallyMissing);

            LuauTestHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauTestHost.Bootstrap, out var bootstrapError), bootstrapError);
            Assert.True(state.TryExecute(
                "OMNI.test.pass(); OMNI.test.fail('broken'); return OMNI.has('test')",
                out var hasTest), hasTest);

            Assert.Equal(1, passes);
            Assert.Equal("broken", failure);
            Assert.Equal("true", hasTest);
        }
        finally
        {
            LuauTestHost.Pass = null;
            LuauTestHost.Fail = null;
        }
    }
}
