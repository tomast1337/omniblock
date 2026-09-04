using OmniBlock.Client.UI.Layout;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.Core;

public class ScrollView : UIElement
{
    private float _dragInitialScrollY;
    private float _dragStartY;
    private bool _isDraggingContent;

    private bool _isDraggingScrollbar;

    public ScrollView()
    {
        ClipToBounds = true;
        ContentContainer = new UIElement
        {
            Style =
            {
                FlexDirection = FlexDirection.Column
            },
            Parent = this
        };

        OnMouseDown += e =>
        {
            if (e.Button != MouseButton.Left) return;
            var relativeX = e.MouseX - ScreenX;
            if (relativeX >= ComputedWidth - 10)
            {
                _isDraggingScrollbar = true;
                _dragStartY = e.MouseY;

                var viewRatio = Math.Min(1.0f, ComputedHeight / ContentContainer.ComputedHeight);
                var barHeight = Math.Max(32f, ComputedHeight * viewRatio);
                var maxBarScroll = ComputedHeight - barHeight;
                var scrollProgress = MaxScrollY > 0 ? ScrollY / MaxScrollY : 0;

                _dragInitialScrollY = scrollProgress * maxBarScroll;
            }
            else
            {
                _isDraggingContent = true;
                _dragStartY = e.MouseY;
                _dragInitialScrollY = ScrollY;
            }
        };

        OnMouseMove += e =>
        {
            if (_isDraggingScrollbar)
            {
                var dragDelta = e.MouseY - _dragStartY;

                var viewRatio = Math.Min(1.0f, ComputedHeight / ContentContainer.ComputedHeight);
                var barHeight = Math.Max(32f, ComputedHeight * viewRatio);
                var maxBarScroll = ComputedHeight - barHeight;

                if (maxBarScroll > 0)
                {
                    var newThumbY = _dragInitialScrollY + dragDelta;
                    var scrollProgress = Math.Clamp(newThumbY / maxBarScroll, 0, 1);
                    ScrollY = scrollProgress * MaxScrollY;
                    FixContentOffset();
                }

                e.Handled = true;
            }
            else if (_isDraggingContent)
            {
                var dragDelta = e.MouseY - _dragStartY;
                ScrollY = _dragInitialScrollY - dragDelta;
                ScrollY = Math.Clamp(ScrollY, 0, MaxScrollY);
                FixContentOffset();
                e.Handled = true;
            }
        };

        OnMouseUp += e =>
        {
            _isDraggingScrollbar = false;
            _isDraggingContent = false;
        };

        OnMouseScroll += e =>
        {
            if (!(MaxScrollY > 0)) return;
            ScrollY -= e.ScrollDelta / 120.0f * 20.0f;
            ScrollY = Math.Clamp(ScrollY, 0, MaxScrollY);
            FixContentOffset();
            e.Handled = true;
        };
    }

    public UIElement ContentContainer { get; }

    public float ScrollY { get; set; }
    public float MaxScrollY => Math.Max(0, ContentContainer.ComputedHeight - ComputedHeight);

    public void ScrollBy(float delta)
    {
        ScrollY = Math.Clamp(ScrollY + delta, 0, MaxScrollY);
        FixContentOffset();
    }

    public void AddContent(UIElement child) => ContentContainer.AddChild(child);

    public override UIElement? HitTest(float screenX, float screenY)
    {
        if (ClipToBounds && !ContainsPoint(screenX, screenY)) return null;

        var hitContent = ContentContainer.HitTest(screenX, screenY);
        if (hitContent != null) return hitContent;

        for (var i = Children.Count - 1; i >= 0; i--)
        {
            var hitChild = Children[i].HitTest(screenX, screenY);
            if (hitChild != null) return hitChild;
        }

        return ContainsPoint(screenX, screenY) ? this : null;
    }

    public override void OnLayoutApplied(LayoutAppliedContext context)
    {
        base.OnLayoutApplied(context);

        FlexLayout.LayoutContext layoutContext = new()
        {
            Root = ContentContainer,
            AvailableWidth = ComputedWidth - 10,
            AvailableHeight = 999999f,
            MeasureString = context.MeasureString
        };

        FlexLayout.ApplyLayout(layoutContext);

        var calculatedHeight = 0f;
        foreach (var child in ContentContainer.Children)
        {
            calculatedHeight = Math.Max(calculatedHeight, child.ComputedY + child.ComputedHeight + child.Style.MarginBottom);
        }

        ContentContainer.ComputedHeight = calculatedHeight;

        ScrollY = Math.Clamp(ScrollY, 0, MaxScrollY);
        FixContentOffset();
    }

    private void FixContentOffset() => ContentContainer.Arrange(0, -ScrollY, ComputedWidth - 10, ContentContainer.ComputedHeight);

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);
        ContentContainer.Update(partialTicks);
    }

    public override void Render(UIRenderer renderer)
    {
        renderer.EnableClipping(0, 0, (int)ComputedWidth, (int)ComputedHeight);

        renderer.PushTranslate(ContentContainer.ComputedX, ContentContainer.ComputedY);

        if (ContentContainer.Style.BackgroundColor is { } bg)
        {
            renderer.DrawRect(0, 0, ContentContainer.ComputedWidth, ContentContainer.ComputedHeight, bg);
        }

        var visibleTop = ScrollY;
        var visibleBottom = ScrollY + ComputedHeight;

        foreach (var child in ContentContainer.Children)
        {
            if (child.ComputedY + child.ComputedHeight < visibleTop || child.ComputedY > visibleBottom)
            {
                continue;
            }

            renderer.PushTranslate(child.ComputedX, child.ComputedY);
            child.Render(renderer);
            renderer.PopTranslate();
        }

        renderer.PopTranslate();

        renderer.DisableClipping();

        if (!(MaxScrollY > 0) || !(ContentContainer.ComputedHeight > 0)) return;

        // track
        renderer.DrawRect(ComputedWidth - 10, 0, 10, ComputedHeight, Color.BlackAlphaC0);

        var viewRatio = Math.Min(1.0f, ComputedHeight / ContentContainer.ComputedHeight);
        var barHeight = Math.Max(32f, ComputedHeight * viewRatio);

        var scrollProgress = ScrollY / MaxScrollY;
        var barY = scrollProgress * (ComputedHeight - barHeight);

        renderer.DrawRect(ComputedWidth - 10, barY, 10, barHeight, Color.Gray80);
        renderer.DrawRect(ComputedWidth - 10, barY, 9, barHeight - 1, new Color(192, 192, 192));
    }
}
