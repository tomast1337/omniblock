using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauDomHostIntegrationTests
{
    [SkippableFact]
    public void Bootstrap_exposesDomProxyWithPersistentPropertyAccess()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        bool visible = true;

        LuauDomHost.Query = selector => selector == "#root" ? 7 : 0;
        LuauDomHost.Parent = _ => 0;
        LuauDomHost.ChildCount = _ => 0;
        LuauDomHost.Child = (_, _) => 0;
        LuauDomHost.GetString = (_, property) => property == "type" ? "Panel" : null;
        LuauDomHost.SetString = (_, _, _) => false;
        LuauDomHost.GetBool = (_, property) => property == "visible" ? visible : null;
        LuauDomHost.SetBool = (_, property, value) =>
        {
            if (property != "visible") return false;
            visible = value;
            return true;
        };

        try
        {
            LuauDomHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out string bootstrapError), bootstrapError);
            Assert.True(state.TryExecute("OMNI.ui.root.type", out string type));
            Assert.True(state.TryExecute("OMNI.ui.root.visible = false", out _));
            Assert.True(state.TryExecute("OMNI.ui.root.visible", out string currentVisibility));
            Assert.True(state.TryExecute("OMNI.environment", out string environment));
            Assert.True(state.TryExecute("OMNI.has(\"ui\")", out string hasUi));

            Assert.Equal("Panel", type);
            Assert.False(visible);
            Assert.Equal("false", currentVisibility);
            Assert.Equal("client", environment);
            Assert.Equal("true", hasUi);
        }
        finally
        {
            LuauDomHost.Query = null;
            LuauDomHost.Parent = null;
            LuauDomHost.ChildCount = null;
            LuauDomHost.Child = null;
            LuauDomHost.GetString = null;
            LuauDomHost.SetString = null;
            LuauDomHost.GetBool = null;
            LuauDomHost.SetBool = null;
        }
    }
}
