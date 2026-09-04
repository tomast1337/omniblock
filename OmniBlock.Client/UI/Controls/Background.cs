using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls;

public enum BackgroundType
{
    Dirt,
    World,
    GameOver
}

public class Background : FullscreenElement
{
    public Background()
    {
    }

    public Background(BackgroundType type) => Type = type;
    public BackgroundType Type { get; set; } = BackgroundType.Dirt;
    public string TexturePath { get; set; } = "/gui/background.png";
    public float Scale { get; set; } = 32.0f;

    public override void Render(UIRenderer renderer)
    {
        if (Type == BackgroundType.World)
        {
            renderer.DrawGradientRect(0, 0, ComputedWidth, ComputedHeight, Color.WorldBackgroundDark, Color.WorldBackground);
        }
        else if (Type == BackgroundType.GameOver)
        {
            renderer.DrawGradientRect(0, 0, ComputedWidth, ComputedHeight, Color.GameOverBackgroundDarkRed, Color.GameOverBackgroundRed);
        }
        else
        {
            var texture = renderer.TextureManager.GetTextureId(TexturePath);
            renderer.DrawRepeatingTexture(texture, 0, 0, ComputedWidth, ComputedHeight, Scale);
        }

        base.Render(renderer);
    }
}
