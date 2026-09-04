using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauClientStateHostIntegrationTests
{
    [SkippableFact]
    public void BootstrapExposesLiveReadOnlyReadinessState()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        var worldLoaded = false;
        var playerReady = false;
        string? worldId = null;
        LuauClientStateHost.WorldLoaded = () => worldLoaded;
        LuauClientStateHost.PlayerReady = () => playerReady;
        LuauClientStateHost.WorldId = () => worldId;

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            LuauClientStateHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauClientStateHost.Bootstrap, out var bootstrapError), bootstrapError);

            AssertValue(state, "OMNI.client.state.worldLoaded", "false");
            AssertValue(state, "OMNI.client.state.playerReady", "false");
            AssertValue(state, "OMNI.client.state.worldId", "nil");

            worldLoaded = true;
            playerReady = true;
            worldId = "e2e-smoke";

            AssertValue(state, "OMNI.client.state.worldLoaded", "true");
            AssertValue(state, "OMNI.client.state.playerReady", "true");
            AssertValue(state, "OMNI.client.state.worldId", "e2e-smoke");
            Assert.False(state.TryExecute("OMNI.client.state.worldLoaded = false", out var readOnlyError));
            Assert.Contains("read-only", readOnlyError);
        }
        finally
        {
            LuauClientStateHost.WorldLoaded = null;
            LuauClientStateHost.PlayerReady = null;
            LuauClientStateHost.WorldId = null;
        }
    }

    private static void AssertValue(LuauState state, string expression, string expected)
    {
        Assert.True(state.TryExecute(expression, out var value), value);
        Assert.Equal(expected, value);
    }
}
