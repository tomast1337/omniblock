using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Worlds.Storage;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class WorldListItem(WorldSaveInfo value) : ListItem<WorldSaveInfo>(value)
{
    private long _hoverStartMs;
    private bool _wasHovered;

    public override void Render(UIRenderer renderer)
    {
        if (IsHovered && !_wasHovered)
        {
            _hoverStartMs = Environment.TickCount64;
        }

        _wasHovered = IsHovered;

        base.Render(renderer);

        string displayName = Value.DisplayName;
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = Translations.Get("world.world"); // Fallback
        }

        renderer.DrawText(displayName, 5, 5, Color.White);

        const string dateFormatPattern = "MMM d, yyyy HH:mm";
        DateTime lastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(Value.LastPlayed).ToLocalTime().DateTime;

        string secondary = $"{Value.FileName} ({lastPlayed.ToString(dateFormatPattern)}, {Value.Size / 1024L / 1024.0F:F2} MB)";

        if (Value.IsUnsupported)
        {
            secondary = Translations.Get("world.unsupportedFormat") + " " + secondary;
        }

        renderer.DrawScrollingText(secondary, 5, 17, (int)ComputedWidth, (int)ComputedHeight, Color.GrayA0, IsHovered ? _hoverStartMs : 0L);
    }
}
