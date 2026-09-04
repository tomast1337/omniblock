using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Controls.Core;

namespace OmniBlock.Tests.UI;

public sealed class UiDomDocumentTests
{
    [Fact]
    public void Query_and_properties_controlLiveTree()
    {
        UIElement root = new();
        Label label = new()
        {
            Text = "before"
        };
        root.AddChild(label);
        UiDomDocument document = new(() => root, () => null);

        var handle = document.Query("Label");

        Assert.NotEqual(0, handle);
        Assert.Equal("Label", document.GetString(handle, "type"));
        Assert.Equal("before", document.GetString(handle, "text"));
        Assert.True(document.SetString(handle, "text", "after"));
        Assert.True(document.SetBool(handle, "visible", false));
        Assert.Equal("after", label.Text);
        Assert.False(label.Visible);
    }

    [Fact]
    public void TextField_text_assignment_notifiesTheLiveControl()
    {
        UIElement root = new();
        string? changed = null;
        TextField field = new()
        {
            AutomationId = "field",
            Text = "before"
        };
        field.OnTextChanged += value => changed = value;
        root.AddChild(field);
        UiDomDocument document = new(() => root, () => null);

        var handle = document.Query("#field");

        Assert.Equal("before", document.GetString(handle, "text"));
        Assert.True(document.SetString(handle, "text", "after"));
        Assert.Equal("after", field.Text);
        Assert.Equal("after", changed);
    }

    [Fact]
    public void Parent_and_child_useStableHandlesWithinOneTree()
    {
        UIElement root = new();
        UIElement child = new();
        root.AddChild(child);
        UiDomDocument document = new(() => root, () => null);

        var rootHandle = document.Query("#root");
        var childHandle = document.GetChild(rootHandle, 0);

        Assert.Equal(1, document.GetChildCount(rootHandle));
        Assert.Equal(rootHandle, document.GetParent(childHandle));
    }

    [Fact]
    public void Query_and_children_includeScrollViewContent()
    {
        UIElement root = new();
        ScrollView scroll = new()
        {
            AutomationId = "list"
        };
        Button item = new(() => { })
        {
            AutomationId = "list.item"
        };
        scroll.AddContent(item);
        root.AddChild(scroll);
        UiDomDocument document = new(() => root, () => null);

        var scrollHandle = document.Query("#list");
        var itemHandle = document.Query("#list.item");

        Assert.NotEqual(0, itemHandle);
        Assert.Equal(1, document.GetChildCount(scrollHandle));
        Assert.Equal(scrollHandle, document.GetParent(document.GetChild(scrollHandle, 0)));
        Assert.True(document.Click(itemHandle));
    }

    [Fact]
    public void ChangingScreenInvalidatesOldHandles()
    {
        UIElement first = new();
        UIElement second = new();
        var current = first;
        UiDomDocument document = new(() => current, () => null);
        var oldHandle = document.Query("#root");

        current = second;

        Assert.Null(document.GetBool(oldHandle, "visible"));
        Assert.NotEqual(oldHandle, document.Query("#root"));
    }

    [Fact]
    public void AutomationId_queryAndClick_activateTheLiveControl()
    {
        UIElement root = new();
        var clicks = 0;
        Button button = new(() => { })
        {
            AutomationId = "main.singleplayer"
        };
        button.OnClick += _ => clicks++;
        root.AddChild(button);
        UiDomDocument document = new(() => root, () => null);

        var handle = document.Query("#main.singleplayer");

        Assert.NotEqual(0, handle);
        Assert.Equal("main.singleplayer", document.GetString(handle, "id"));
        Assert.True(document.Click(handle));
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Click_dispatchesTheMouseGestureUsedByOptionControls()
    {
        UIElement root = new();
        var mouseDowns = 0;
        Button button = new(() => { })
        {
            AutomationId = "option"
        };
        button.OnMouseDown += _ => mouseDowns++;
        root.AddChild(button);
        UiDomDocument document = new(() => root, () => null);

        Assert.True(document.Click(document.Query("#option")));
        Assert.Equal(1, mouseDowns);
    }

    [Theory]
    [InlineData("visible")]
    [InlineData("enabled")]
    [InlineData("hitTestVisible")]
    public void Click_rejectsNonInteractableControlsAndAncestors(string blockedProperty)
    {
        UIElement root = new();
        UIElement parent = new();
        Button button = new(() => { })
        {
            AutomationId = "button"
        };
        parent.AddChild(button);
        root.AddChild(parent);
        UiDomDocument document = new(() => root, () => null);
        var handle = document.Query("#button");

        Assert.True(document.SetBool(document.GetParent(handle), blockedProperty, false));

        Assert.False(document.Click(handle));
    }

    [Fact]
    public void RemovingAControlInvalidatesItsHandle()
    {
        UIElement root = new();
        Button button = new(() => { })
        {
            AutomationId = "button"
        };
        root.AddChild(button);
        UiDomDocument document = new(() => root, () => null);
        var handle = document.Query("#button");

        root.RemoveChild(button);

        Assert.False(document.Click(handle));
        Assert.Null(document.GetString(handle, "id"));
    }

    [Fact]
    public void ClearingChildrenInvalidatesHandlesEvenWhenLegacyParentPointersRemain()
    {
        UIElement root = new();
        Button button = new(() => { })
        {
            AutomationId = "button"
        };
        root.AddChild(button);
        UiDomDocument document = new(() => root, () => null);
        var handle = document.Query("#button");

        root.Children.Clear();

        Assert.False(document.Click(handle));
    }
}
