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
        double meshPending = 0;
        double meshRequestToGpuMs = 0;
        double frameTimeMs = 0;
        double safetyLoaded = 0;
        double safetyExpected = 0;
        double safetyHoles = 0;
        double meshReadyRadius = 0;
        LuauClientStateHost.WorldLoaded = () => worldLoaded;
        LuauClientStateHost.PlayerReady = () => playerReady;
        LuauClientStateHost.WorldId = () => worldId;
        LuauClientStateHost.MeshPending = () => meshPending;
        LuauClientStateHost.MeshRequestToGpuMs = () => meshRequestToGpuMs;
        LuauClientStateHost.FrameTimeMs = () => frameTimeMs;
        LuauClientStateHost.MeshSafetyLoadedColumns = () => safetyLoaded;
        LuauClientStateHost.MeshSafetyExpectedSections = () => safetyExpected;
        LuauClientStateHost.MeshSafetyHoles = () => safetyHoles;
        LuauClientStateHost.MeshReadyRadius = () => meshReadyRadius;

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            LuauClientStateHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauClientStateHost.Bootstrap, out var bootstrapError), bootstrapError);

            AssertValue(state, "OMNI.client.state.worldLoaded", "false");
            AssertValue(state, "OMNI.client.state.playerReady", "false");
            AssertValue(state, "OMNI.client.state.worldId", "nil");
            AssertValue(state, "OMNI.client.state.meshPending", "0");
            AssertValue(state, "OMNI.client.state.meshRequestToGpuMs", "0");
            AssertValue(state, "OMNI.client.state.frameTimeMs", "0");
            AssertValue(state, "OMNI.client.state.meshSafetyHoles", "0");

            worldLoaded = true;
            playerReady = true;
            worldId = "e2e-smoke";
            meshPending = 7;
            meshRequestToGpuMs = 12.5;
            frameTimeMs = 6.25;
            safetyLoaded = 29;
            safetyExpected = 232;
            safetyHoles = 3;
            meshReadyRadius = 2;

            AssertValue(state, "OMNI.client.state.worldLoaded", "true");
            AssertValue(state, "OMNI.client.state.playerReady", "true");
            AssertValue(state, "OMNI.client.state.worldId", "e2e-smoke");
            AssertValue(state, "OMNI.client.state.meshPending", "7");
            AssertValue(state, "OMNI.client.state.meshRequestToGpuMs", "12.5");
            AssertValue(state, "OMNI.client.state.frameTimeMs", "6.25");
            AssertValue(state, "OMNI.client.state.meshSafetyLoadedColumns", "29");
            AssertValue(state, "OMNI.client.state.meshSafetyExpectedSections", "232");
            AssertValue(state, "OMNI.client.state.meshSafetyHoles", "3");
            AssertValue(state, "OMNI.client.state.meshReadyRadius", "2");
            Assert.False(state.TryExecute("OMNI.client.state.worldLoaded = false", out var readOnlyError));
            Assert.Contains("read-only", readOnlyError);
        }
        finally
        {
            LuauClientStateHost.WorldLoaded = null;
            LuauClientStateHost.PlayerReady = null;
            LuauClientStateHost.WorldId = null;
            LuauClientStateHost.MeshPending = null;
            LuauClientStateHost.MeshRequestToGpuMs = null;
            LuauClientStateHost.FrameTimeMs = null;
            LuauClientStateHost.MeshSafetyLoadedColumns = null;
            LuauClientStateHost.MeshSafetyExpectedSections = null;
            LuauClientStateHost.MeshSafetyHoles = null;
            LuauClientStateHost.MeshReadyRadius = null;
        }
    }

    private static void AssertValue(LuauState state, string expression, string expected)
    {
        Assert.True(state.TryExecute(expression, out var value), value);
        Assert.Equal(expected, value);
    }
}
