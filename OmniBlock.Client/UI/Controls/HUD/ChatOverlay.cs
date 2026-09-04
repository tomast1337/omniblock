using System.Text;
using System.Text.RegularExpressions;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.HUD;

public class ChatOverlay : UIElement
{
    private const int MaxHistoryLines = 20;

    private readonly List<ChatLine> _messages = [];

    private readonly Func<float> _scaleFunc;
    private readonly Func<float> _widthFunc;
    private int _charactersPerLine = 64;
    private int _chatWidth;
    private int _lastCharactersPerLine = 64;
    private int _lineHeight;
    private string? _recordPlaying;
    private int _recordPlayingTimer;
    private float _scale = 1.0f;

    public ChatOverlay(Func<float> scaleFunc, Func<float> widthFunc)
    {
        _scaleFunc = scaleFunc;
        _widthFunc = widthFunc;
        UpdateScale();

        Style.Height = null; // Auto wrap
    }

    public int ScrollOffset { get; set; }
    public string? HoveredItemName { get; set; }
    public bool IsOpen { get; set; }

    private void UpdateScale()
    {
        _scale = _scaleFunc();
        _chatWidth = (int)(320 * _scale) & ~1;
        _lineHeight = _chatWidth / 35;
        Style.Width = _chatWidth;
        var extraWidth = _widthFunc() * 32;
        _chatWidth += (int)(extraWidth * 5);
        _charactersPerLine = 64 + (int)extraWidth;

        // trim messages if needed
        if (_lastCharactersPerLine > _charactersPerLine)
        {
            foreach (var m in _messages)
            {
                var c = m.Message.Where(t => t == '§').Sum(_ => 2);

                c = _charactersPerLine - c;
                if (m.Message.Length > c)
                {
                    m.Message = m.Message.Substring(0, c);
                }
            }
        }

        _lastCharactersPerLine = _charactersPerLine;
    }

    public void AddMessage(string message)
    {
        var currentColor = "";
        StringBuilder currentLine = new();
        var visibleLength = 0;

        // Split while preserving spaces
        var words = Regex.Split(message, @"(\s+)");

        foreach (var word in words)
        {
            var wordVisibleLength = GetVisibleLength(word);

            // Wrap before adding the word
            if (visibleLength > 0 &&
                visibleLength + wordVisibleLength > _charactersPerLine)
            {
                _messages.Insert(0, new ChatLine(currentLine.ToString()));

                currentLine.Clear();

                // Carry active color to next line
                if (!string.IsNullOrEmpty(currentColor))
                {
                    currentLine.Append(currentColor);
                }

                visibleLength = 0;
            }

            // Append word while tracking colors
            for (var i = 0; i < word.Length; i++)
            {
                var c = word[i];

                // Color code (§x)
                if (c == '§' && i + 1 < word.Length)
                {
                    currentColor = "§" + word[i + 1];
                    currentLine.Append(currentColor);
                    i++;
                    continue;
                }

                currentLine.Append(c);
                visibleLength++;
            }
        }

        // Add final line
        if (currentLine.Length > 0)
        {
            _messages.Insert(0, new ChatLine(currentLine.ToString()));
        }

        // Limit history
        while (_messages.Count > 100)
        {
            _messages.RemoveAt(_messages.Count - 1);
        }
    }

    private int GetVisibleLength(string text)
    {
        var length = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '§' && i + 1 < text.Length)
            {
                i++; // Skip color character
                continue;
            }

            length++;
        }

        return length;
    }

    public void ClearMessages() => _messages.Clear();

    public void SetRecordPlaying(string recordName)
    {
        _recordPlaying = $"Now playing: {recordName}";
        _recordPlayingTimer = 120; // 6 seconds
    }

    public void ScrollMessages(int amount)
    {
        ScrollOffset += amount;
        if (ScrollOffset < 0)
        {
            ScrollOffset = 0;
        }

        var maxScroll = Math.Max(0, _messages.Count - MaxHistoryLines);
        if (ScrollOffset > maxScroll)
        {
            ScrollOffset = maxScroll;
        }
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);
        foreach (var msg in _messages)
        {
            msg.UpdateCounter++;
        }

        if (_recordPlayingTimer > 0)
        {
            _recordPlayingTimer--;
        }
    }

    public override void Render(UIRenderer renderer)
    {
        UpdateScale();
        if (IsOpen)
        {
            RenderHistory(renderer);
        }
        else
        {
            RenderFading(renderer);
        }

        // Render record playing
        if (_recordPlayingTimer > 0 && _recordPlaying != null)
        {
            renderer.DrawCenteredText(_recordPlaying, 160, -40, Color.White, shadow: true);
        }

        base.Render(renderer);
    }

    private void RenderFading(UIRenderer renderer)
    {
        var yOffset = 0;
        for (var i = 0; i < _messages.Count && i < 10; i++)
        {
            var msg = _messages[i];
            if (msg.UpdateCounter < 200)
            {
                var progress = msg.UpdateCounter / 200.0f;
                var alpha = Math.Clamp((1.0f - progress) * 10.0f, 0, 1);
                alpha *= alpha; // Non-linear fade out

                renderer.DrawRect(0, yOffset - _lineHeight, _chatWidth, _lineHeight, new Color(0, 0, 0, (byte)(100 * alpha)));
                renderer.DrawText(msg.Message, 0, yOffset - _lineHeight, new Color(255, 255, 255, (byte)(255 * alpha)), _scale);
                yOffset -= _lineHeight;
            }
        }
    }

    private void RenderHistory(UIRenderer renderer)
    {
        var visibleCount = Math.Min(MaxHistoryLines, _messages.Count - ScrollOffset);
        if (visibleCount <= 0)
        {
            return;
        }

        // Render messages bottom-up
        var yOffset = 0;
        for (var i = ScrollOffset; i < ScrollOffset + visibleCount; i++)
        {
            renderer.DrawRect(0, yOffset - _lineHeight, _chatWidth, _lineHeight, new Color(0, 0, 0, 100));
            renderer.DrawText(_messages[i].Message, 0, yOffset - _lineHeight, Color.White, _scale);
            yOffset -= _lineHeight;
        }

        // Scroll indicator when not at the bottom
        if (ScrollOffset > 0)
        {
            renderer.DrawText("^  ^  ^", _chatWidth / 2 - 20, yOffset - _lineHeight, new Color(255, 255, 255, 180), shadow: false);
        }
    }

    private class ChatLine(string message)
    {
        public string Message = message;
        public int UpdateCounter;
    }
}
