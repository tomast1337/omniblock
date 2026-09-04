using OmniBlock.Client.Rendering;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.Core;

public partial class TextField : UIElement
{
    private readonly TextBuffer _buffer = new();
    private int _cursorCounter;

    private bool _isDragging;
    private TextRenderer? _textRenderer;
    public Action? OnSubmit;

    public Action<string>? OnTextChanged;

    public TextField()
    {
        Style.Width = 200;
        Style.Height = 20;

        OnMouseEnter += _ => IsHovered = true;
        OnMouseLeave += _ => IsHovered = false;

        OnMouseDown += e =>
        {
            if (e.Button != MouseButton.Left) return;

            e.Handled = true;
            if (_textRenderer is null) return;

            _buffer.MoveTo(GetCursorIndexAt(e.MouseX - ScreenX), false);
            _isDragging = true;
        };

        OnMouseMove += e =>
        {
            if (_isDragging && _textRenderer is not null)
            {
                _buffer.MoveTo(GetCursorIndexAt(e.MouseX - ScreenX), true);
            }
        };

        OnMouseUp += e =>
        {
            if (e.Button == MouseButton.Left)
            {
                _isDragging = false;
            }
        };

        OnKeyDown += HandleKeyDown;
    }

    public string Text
    {
        get => _buffer.Text;
        set => _buffer.Text = value;
    }

    public string Placeholder { get; set; } = "";

    public override bool DoTextMeasuring => true;

    public int MaxLength
    {
        get => _buffer.MaxLength;
        set => _buffer.MaxLength = value;
    }

    public int CursorPosition
    {
        get => _buffer.CursorPosition;
        set => _buffer.CursorPosition = value;
    }

    public int SelectionStart
    {
        get => _buffer.SelectionStart;
        set => _buffer.SelectionStart = value;
    }

    public override void Update(float partialTicks)
    {
        if (IsFocused)
        {
            _cursorCounter++;
        }
        else
        {
            _cursorCounter = 0;
            _buffer.ClearSelection();
        }

        base.Update(partialTicks);
    }

    public override void Render(UIRenderer renderer)
    {
        _textRenderer = renderer.TextRenderer;

        DrawBox(renderer);

        if (string.IsNullOrEmpty(Text) && !IsFocused)
        {
            renderer.DrawText(Placeholder, 4, ComputedHeight / 2 - 4, Color.Gray70);
        }
        else
        {
            // Selection Highlight
            if (_buffer.HasSelection)
            {
                DrawSelectionHighlight(renderer);
            }

            renderer.DrawText(Text, 4, ComputedHeight / 2 - 4, Color.White);

            if (IsFocused && _cursorCounter / 10 % 2 == 0)
            {
                var cursorX =
                    4 + renderer.TextRenderer.GetStringWidth(Text[.._buffer.CursorPosition]);
                renderer.DrawRect(cursorX, ComputedHeight / 2 - 5, 1, 10, Color.White);
            }
        }

        base.Render(renderer);
    }

    public override List<string> GetInspectorProperties()
    {
        var props = base.GetInspectorProperties();
        props.Add($"Text:     \"{_buffer.Text}\"");
        props.Add($"Placeholder: \"{Placeholder}\"");
        props.Add($"MaxLength: {MaxLength}   Cursor: {CursorPosition}  SelectionStart: {_buffer.SelectionStart}");
        return props;
    }

    private void DrawBox(UIRenderer renderer)
    {
        renderer.DrawRect(0, 0, ComputedWidth, ComputedHeight, Color.Black);

        var borderColor = IsFocused ? Color.White : IsHovered ? Color.GrayCC : Color.GrayA0;
        renderer.DrawRect(0, 0, ComputedWidth, 1, borderColor);
        renderer.DrawRect(0, ComputedHeight - 1, ComputedWidth, 1, borderColor);
        renderer.DrawRect(0, 0, 1, ComputedHeight, borderColor);
        renderer.DrawRect(ComputedWidth - 1, 0, 1, ComputedHeight, borderColor);
    }

    private void DrawSelectionHighlight(UIRenderer renderer)
    {
        var start = Math.Min(_buffer.SelectionStart, _buffer.CursorPosition);
        var end = Math.Max(_buffer.SelectionStart, _buffer.CursorPosition);
        var x1 = 4 + renderer.TextRenderer.GetStringWidth(Text[..start]);
        var x2 = 4 + renderer.TextRenderer.GetStringWidth(Text[..end]);
        renderer.DrawRect(x1, ComputedHeight / 2 - 5, x2 - x1, 10, new Color(0, 0, 255, 128));
    }

    private int GetCursorIndexAt(float localX)
    {
        if (_textRenderer == null)
        {
            return 0;
        }

        const float xOffset = 4; // Padding
        if (string.IsNullOrEmpty(Text)) return 0;

        var bestIndex = 0;
        var bestDist = float.MaxValue;

        for (var i = 0; i <= Text.Length; i++)
        {
            float width = _textRenderer.GetStringWidth(Text[..i]);
            var dist = MathF.Abs(xOffset + width - localX);
            if (!(dist < bestDist)) continue;

            bestDist = dist;
            bestIndex = i;
        }

        return bestIndex;
    }
}
