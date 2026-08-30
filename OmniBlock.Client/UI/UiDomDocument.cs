using OmniBlock.Client.UI.Controls.Core;

namespace OmniBlock.Client.UI;

/// <summary>
///     A session-local handle table over the live UI trees. Handles are invalidated when the
///     active screen changes, preventing scripts from retaining dead screens and their resources.
/// </summary>
public sealed class UiDomDocument(Func<UIElement?> screenRoot, Func<UIElement?> hudRoot)
{
    private readonly Dictionary<int, UIElement> _elements = [];
    private readonly Dictionary<UIElement, int> _handles = new(ReferenceEqualityComparer.Instance);
    private UIElement? _lastScreenRoot;
    private UIElement? _lastHudRoot;
    private int _nextHandle = 1;

    public int Query(string selector)
    {
        RefreshRoots();
        UIElement? element = selector.ToLowerInvariant() switch
        {
            "#root" or "#screen" => _lastScreenRoot ?? _lastHudRoot,
            "#hud" => _lastHudRoot,
            _ when selector.StartsWith('#') =>
                FindByAutomationId(_lastScreenRoot, selector[1..]) ??
                FindByAutomationId(_lastHudRoot, selector[1..]),
            _ => FindByType(_lastScreenRoot, selector) ?? FindByType(_lastHudRoot, selector),
        };
        return GetHandle(element);
    }

    public int GetParent(int handle) => Resolve(handle) is { Parent: { } parent } ? GetHandle(parent) : 0;
    public int GetChildCount(int handle) => Resolve(handle)?.Children.Count ?? 0;

    public int GetChild(int handle, int index)
    {
        UIElement? element = Resolve(handle);
        return element != null && (uint)index < (uint)element.Children.Count
            ? GetHandle(element.Children[index])
            : 0;
    }

    public string? GetString(int handle, string property) => Resolve(handle) is { } element
        ? property.ToLowerInvariant() switch
        {
            "type" => element.GetType().Name,
            "id" => element.AutomationId,
            "text" when element is Label label => label.Text,
            "text" when element is Button button => button.Text,
            _ => null,
        }
        : null;

    public bool SetString(int handle, string property, string value)
    {
        if (Resolve(handle) is not { } element || !property.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (element)
        {
            case Label label:
                label.Text = value;
                return true;
            case Button button:
                button.Text = value;
                return true;
            default:
                return false;
        }
    }

    public bool? GetBool(int handle, string property) => Resolve(handle) is { } element
        ? property.ToLowerInvariant() switch
        {
            "visible" => element.Visible,
            "enabled" => element.Enabled,
            "hittestvisible" => element.IsHitTestVisible,
            _ => null,
        }
        : null;

    public bool SetBool(int handle, string property, bool value)
    {
        if (Resolve(handle) is not { } element)
        {
            return false;
        }

        switch (property.ToLowerInvariant())
        {
            case "visible": element.Visible = value; return true;
            case "enabled": element.Enabled = value; return true;
            case "hittestvisible": element.IsHitTestVisible = value; return true;
            default: return false;
        }
    }

    public bool Click(int handle)
    {
        if (Resolve(handle) is not { OnClick: not null } element || !IsInteractable(element))
        {
            return false;
        }

        element.OnClick(new UIMouseEvent
        {
            Target = element,
            MouseX = (int)(element.ScreenX + element.ComputedWidth / 2),
            MouseY = (int)(element.ScreenY + element.ComputedHeight / 2),
            Button = MouseButton.Left,
        });
        return true;
    }

    private UIElement? Resolve(int handle)
    {
        RefreshRoots();
        UIElement? element = _elements.GetValueOrDefault(handle);
        if (element == null || !IsInLiveTree(element))
        {
            return null;
        }

        return element;
    }

    private int GetHandle(UIElement? element)
    {
        if (element == null)
        {
            return 0;
        }

        if (_handles.TryGetValue(element, out int handle))
        {
            return handle;
        }

        handle = _nextHandle++;
        _handles[element] = handle;
        _elements[handle] = element;
        return handle;
    }

    private void RefreshRoots()
    {
        UIElement? currentScreenRoot = screenRoot();
        UIElement? currentHudRoot = hudRoot();
        if (ReferenceEquals(currentScreenRoot, _lastScreenRoot) && ReferenceEquals(currentHudRoot, _lastHudRoot))
        {
            return;
        }

        _lastScreenRoot = currentScreenRoot;
        _lastHudRoot = currentHudRoot;
        _elements.Clear();
        _handles.Clear();
    }

    private static UIElement? FindByType(UIElement? root, string selector)
    {
        if (root == null)
        {
            return null;
        }

        if (root.GetType().Name.Equals(selector, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        foreach (UIElement child in root.Children)
        {
            if (FindByType(child, selector) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static UIElement? FindByAutomationId(UIElement? root, string id)
    {
        if (root == null)
        {
            return null;
        }

        if (string.Equals(root.AutomationId, id, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (UIElement child in root.Children)
        {
            if (FindByAutomationId(child, id) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private bool IsInLiveTree(UIElement element)
    {
        UIElement root = element;
        while (root.Parent is { } parent)
        {
            // Some existing screens rebuild lists with Children.Clear(), which does not reset
            // each former child's Parent pointer. Verify both sides of the relationship so a
            // retained handle cannot activate one of those detached controls.
            if (!parent.Children.Contains(root))
            {
                return false;
            }

            root = parent;
        }

        return ReferenceEquals(root, _lastScreenRoot) || ReferenceEquals(root, _lastHudRoot);
    }

    private static bool IsInteractable(UIElement element)
    {
        for (UIElement? current = element; current != null; current = current.Parent)
        {
            if (!current.Visible || !current.Enabled || !current.IsHitTestVisible)
            {
                return false;
            }
        }

        return true;
    }
}
