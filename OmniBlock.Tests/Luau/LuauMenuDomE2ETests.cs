using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Full scripting-boundary check: a real UIElement menu tree is exposed through the same
///     handle table and unmanaged Host facade as the client, then discovered and asserted by
///     Luau code without the test handing individual controls to the script.
/// </summary>
[Collection(LuauHostCollection.Name)]
public sealed class LuauMenuDomE2ETests
{
    [SkippableFact]
    public void Luau_canWalkMenuAndAssertExpectedControls()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");

        UIElement menuRoot = BuildMenuFixture();
        UiDomDocument document = new(() => menuRoot, () => null);
        Bind(document);

        using LuauState state = new();
        state.ResetInstructionBudget(100_000);

        try
        {
            LuauDomHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out string bootstrapError), bootstrapError);

            const string assertions = """
local found = {}
local function walk(node)
    if node.text ~= nil then found[node.text] = node.type end
    for index = 1, node.childCount do walk(node:child(index)) end
end

walk(OMNI.ui.root)
assert(found["Singleplayer"] == "Button", "Singleplayer button missing")
assert(found["Multiplayer"] == "Button", "Multiplayer button missing")
assert(found["Options..."] == "Button", "Options button missing")
assert(found["Quit Game"] == "Button", "Quit button missing")
assert(found["OmniBlock"] == "Label", "title label missing")
return "menu DOM OK"
""";

            Assert.True(state.TryExecute(assertions, out string output), output);
            Assert.Equal("menu DOM OK", output);
        }
        finally
        {
            Unbind();
        }
    }

    private static UIElement BuildMenuFixture()
    {
        UIElement root = new();
        root.AddChild(new Label { Text = "OmniBlock" });
        root.AddChild(new Button(() => { }) { Text = "Singleplayer", AutomationId = "main.singleplayer" });
        root.AddChild(new Button(() => { }) { Text = "Multiplayer" });
        UIElement footer = new Panel();
        footer.AddChild(new Button(() => { }) { Text = "Options..." });
        footer.AddChild(new Button(() => { }) { Text = "Quit Game" });
        root.AddChild(footer);
        return root;
    }

    private static void Bind(UiDomDocument document)
    {
        LuauDomHost.Query = document.Query;
        LuauDomHost.Parent = document.GetParent;
        LuauDomHost.ChildCount = document.GetChildCount;
        LuauDomHost.Child = document.GetChild;
        LuauDomHost.GetString = document.GetString;
        LuauDomHost.SetString = document.SetString;
        LuauDomHost.GetBool = document.GetBool;
        LuauDomHost.SetBool = document.SetBool;
        LuauDomHost.Click = document.Click;
    }

    private static void Unbind()
    {
        LuauDomHost.Query = null;
        LuauDomHost.Parent = null;
        LuauDomHost.ChildCount = null;
        LuauDomHost.Child = null;
        LuauDomHost.GetString = null;
        LuauDomHost.SetString = null;
        LuauDomHost.GetBool = null;
        LuauDomHost.SetBool = null;
        LuauDomHost.Click = null;
    }
}
