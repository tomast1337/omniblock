using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauLogHostIntegrationTests
{
    [SkippableFact]
    public void Print_routesFormattedLineToHostLogger()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(10_000);
        List<string> lines = [];
        LuauLogHost.WriteLine = lines.Add;

        try
        {
            LuauLogHost.Install(state.Handle);

            Assert.True(state.TryExecute("print('player', 42, true)", out string output), output);
            Assert.Equal(["player\t42\ttrue"], lines);
        }
        finally
        {
            LuauLogHost.WriteLine = null;
        }
    }
}
