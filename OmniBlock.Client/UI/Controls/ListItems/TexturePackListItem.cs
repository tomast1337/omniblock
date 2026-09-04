using OmniBlock.Client.Resource.Pack;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class TexturePackListItem(TexturePack value) : ListItem<TexturePack>(value)
{
    public override void Render(UIRenderer renderer)
    {
        base.Render(renderer);

        var thumbnail = Value.GetThumbnailTexture(renderer.TextureManager);
        renderer.DrawTexture(thumbnail, 4, 4, 24, 24);

        var fileName = Value.TexturePackFileName;
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "Unknown Pack";
        }

        renderer.DrawText(fileName, 32, 3, Color.White);

        if (!string.IsNullOrEmpty(Value.FirstDescriptionLine))
        {
            renderer.DrawText(Value.FirstDescriptionLine, 32, 12, Color.GrayA0);
        }

        if (!string.IsNullOrEmpty(Value.SecondDescriptionLine))
        {
            renderer.DrawText(Value.SecondDescriptionLine, 32, 21, Color.GrayA0);
        }
    }
}
