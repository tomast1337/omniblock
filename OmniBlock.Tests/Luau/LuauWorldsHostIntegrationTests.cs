using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauWorldsHostIntegrationTests
{
    [SkippableFact]
    public void BootstrapListsWorldMetadataAndQueuesLoadById()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        string? requested = null;
        LuauWorldsHost.List = () =>
        [
            new LuauWorldInfo("World1", "First World", 123, 456, false),
            new LuauWorldInfo("Old", "Old World", 12, 34, true)
        ];
        LuauWorldsHost.Load = id => { requested = id; return id == "World1"; };

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out string omniError), omniError);
            LuauWorldsHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauWorldsHost.Bootstrap, out string worldsError), worldsError);

            Assert.True(state.TryExecute("#OMNI.client.worlds.list()", out string count), count);
            Assert.True(state.TryExecute("OMNI.client.worlds.list()[1].name", out string name), name);
            Assert.True(state.TryExecute("OMNI.client.worlds.list()[2].unsupported", out string unsupported), unsupported);
            Assert.True(state.TryExecute("OMNI.client.worlds.load(\"World1\")", out string accepted), accepted);
            Assert.True(state.TryExecute("OMNI.has(\"worlds\")", out string hasWorlds), hasWorlds);

            Assert.Equal("2", count);
            Assert.Equal("First World", name);
            Assert.Equal("true", unsupported);
            Assert.Equal("true", accepted);
            Assert.Equal("true", hasWorlds);
            Assert.Equal("World1", requested);
        }
        finally
        {
            LuauWorldsHost.List = null;
            LuauWorldsHost.Load = null;
        }
    }
}
