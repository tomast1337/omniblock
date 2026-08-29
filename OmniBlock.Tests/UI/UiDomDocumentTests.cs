using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Controls.Core;

namespace OmniBlock.Tests.UI;

public sealed class UiDomDocumentTests
{
    [Fact]
    public void Query_and_properties_controlLiveTree()
    {
        UIElement root = new();
        Label label = new() { Text = "before" };
        root.AddChild(label);
        UiDomDocument document = new(() => root, () => null);

        int handle = document.Query("Label");

        Assert.NotEqual(0, handle);
        Assert.Equal("Label", document.GetString(handle, "type"));
        Assert.Equal("before", document.GetString(handle, "text"));
        Assert.True(document.SetString(handle, "text", "after"));
        Assert.True(document.SetBool(handle, "visible", false));
        Assert.Equal("after", label.Text);
        Assert.False(label.Visible);
    }

    [Fact]
    public void Parent_and_child_useStableHandlesWithinOneTree()
    {
        UIElement root = new();
        UIElement child = new();
        root.AddChild(child);
        UiDomDocument document = new(() => root, () => null);

        int rootHandle = document.Query("#root");
        int childHandle = document.GetChild(rootHandle, 0);

        Assert.Equal(1, document.GetChildCount(rootHandle));
        Assert.Equal(rootHandle, document.GetParent(childHandle));
    }

    [Fact]
    public void ChangingScreenInvalidatesOldHandles()
    {
        UIElement first = new();
        UIElement second = new();
        UIElement current = first;
        UiDomDocument document = new(() => current, () => null);
        int oldHandle = document.Query("#root");

        current = second;

        Assert.Null(document.GetBool(oldHandle, "visible"));
        Assert.NotEqual(oldHandle, document.Query("#root"));
    }
}
