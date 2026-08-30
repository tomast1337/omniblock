using OmniBlock.Client.UI.Rendering;

namespace OmniBlock.Client.UI;

public class UIElement
{
    /// <summary>
    /// Stable, untranslated identifier used by UI automation. The stored value excludes the
    /// selector's leading '#', for example <c>main.singleplayer</c>.
    /// </summary>
    public string? AutomationId { get; set; }

    public Action<UIMouseEvent>? OnClick;
    public Action<UIKeyEvent>? OnKeyDown;

    // Events
    public Action<UIMouseEvent>? OnMouseDown;
    public Action<UIMouseEvent>? OnMouseEnter;
    public Action<UIMouseEvent>? OnMouseLeave;
    public Action<UIMouseEvent>? OnMouseMove;
    public Action<UIMouseEvent>? OnMouseScroll;
    public Action<UIMouseEvent>? OnMouseUp;
    public UIElement? Parent { get; set; }
    public List<UIElement> Children { get; } = [];

    public bool Visible { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether this element can be hit-tested by the mouse.
    ///     If false, the element and its children will be invisible to mouse events (hover, click, etc.),
    ///     allowing events to pass through to the parent or elements behind it.
    /// </summary>
    public bool IsHitTestVisible { get; set; } = true;

    public FlexStyle Style { get; set; } = new();

    // Computed Layout Box
    public float ComputedX { get; set; }
    public float ComputedY { get; set; }
    public float ComputedWidth { get; set; }
    public float ComputedHeight { get; set; }

    public float ScreenX => (Parent?.ScreenX ?? 0) + ComputedX;
    public float ScreenY => (Parent?.ScreenY ?? 0) + ComputedY;

    public bool IsHovered { get; internal set; }
    public bool IsFocused { get; internal set; }
    public bool Enabled { get; set; } = true;

    public bool ClipToBounds { get; set; } = false;

    public virtual bool DoTextMeasuring => false;

    public void AddChild(UIElement child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void RemoveChild(UIElement child)
    {
        if (child.Parent == this)
        {
            child.Parent = null;
            Children.Remove(child);
        }
    }

    public virtual void Measure(MeasureContext context)
    {
        ComputedWidth = Style.Width ?? context.AvailableWidth;
        ComputedHeight = Style.Height ?? context.AvailableHeight;
    }

    public virtual void Arrange(float x, float y, float width, float height)
    {
        ComputedX = x;
        ComputedY = y;
        ComputedWidth = width;
        ComputedHeight = height;
    }

    public virtual void Update(float partialTicks)
    {
        foreach (UIElement child in Children)
        {
            child.Update(partialTicks);
        }
    }

    public virtual void Render(UIRenderer renderer)
    {
        if (Style.BackgroundColor is { } bg)
        {
            renderer.DrawRect(0, 0, ComputedWidth, ComputedHeight, bg);
        }

        foreach (UIElement child in Children)
        {
            renderer.PushTranslate(child.ComputedX, child.ComputedY);
            child.Render(renderer);
            renderer.PopTranslate();
        }
    }

    // Fired automatically by the layout engine
    public virtual void OnLayoutApplied(LayoutAppliedContext context)
    {
    }

    public virtual UIElement? HitTest(float screenX, float screenY)
    {
        if (!IsHitTestVisible)
        {
            return null;
        }

        if (ClipToBounds && !ContainsPoint(screenX, screenY))
        {
            return null;
        }

        for (int i = Children.Count - 1; i >= 0; i--)
        {
            UIElement? hit = Children[i].HitTest(screenX, screenY);
            if (hit != null)
            {
                return hit;
            }
        }

        if (ContainsPoint(screenX, screenY))
        {
            return this;
        }

        return null;
    }

    public virtual List<string> GetInspectorProperties() =>
    [
        $"Type:     {GetType().FullName}",
        $"ID:       {AutomationId ?? "-"}",
        $"Screen:   ({ScreenX:F1}, {ScreenY:F1})",
        $"Size:     {ComputedWidth:F1} × {ComputedHeight:F1}",
        $"Local:    ({ComputedX:F1}, {ComputedY:F1})",
        $"Visible:  {Visible}   Enabled: {Enabled}",
        $"Focused:  {IsFocused}   Hovered: {IsHovered}",
        $"HitTest:  {IsHitTestVisible}   Clip: {ClipToBounds}",
        $"Children: {Children.Count}"
    ];

    public bool ContainsPoint(float screenX, float screenY)
    {
        float sx = ScreenX;
        float sy = ScreenY;
        return screenX >= sx && screenX < sx + ComputedWidth &&
               screenY >= sy && screenY < sy + ComputedHeight;
    }

    public struct MeasureContext
    {
        public float AvailableWidth;
        public float AvailableHeight;
        public Func<string, float> MeasureString;
    }

    public struct LayoutAppliedContext
    {
        public Func<string, float> MeasureString;
    }
}
