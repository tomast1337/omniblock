using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class LanguageListItem(Language value) : ListItem<Language>(value)
{
    public override void Render(UIRenderer renderer)
    {
        base.Render(renderer);

        Style.Height = 20;

        var displayName = Value.Name;
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = "Unknown"; // Fallback
        }

        renderer.DrawText(displayName, 5, 6, Color.White);
    }
}
