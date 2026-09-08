using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using OmniBlock.Worlds;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class SelectWorldTypeListItem(WorldType type) : ListItem<WorldType>(type)
{
    public override void Render(UIRenderer renderer)
    {
        base.Render(renderer);

        if (!string.IsNullOrEmpty(Value.IconPath))
        {
            var texture = renderer.TextureManager.GetTextureId(Value.IconPath);
            renderer.DrawTexture(texture, 4, 4, 24, 24);
        }
        else
        {
            renderer.DrawRect(4, 4, 24, 24, Color.BackgroundBlackAlpha);
        }

        var translationRoot = $"selectWorld.type.{Value.Name.ToLowerInvariant()}";
        renderer.DrawText(Translations.Get($"{translationRoot}.title"), 32, 4, Color.White);
        renderer.DrawText(Translations.Get($"{translationRoot}.description"), 32, 16, Color.Gray80);
    }
}
