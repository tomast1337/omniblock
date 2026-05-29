using BetaSharp.Client.Guis;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.UI.Rendering;

namespace BetaSharp.Client.UI.Controls.Core;

public class Button : UIElement
{
    public string Text { get; set; } = "";
    public Color TextColor { get; set; } = Color.GrayE0;
    public Color HoverTextColor { get; set; } = Color.HoverYellow;
    public Action ClickSound;

    public override bool DoTextMeasuring => true;


    public Button(Action clickSound)
    {
        Style.Width = 200;
        Style.Height = 20;

        OnClick += (e) =>
        {
            if (Enabled)
            {
                clickSound();
            }
        };

        OnMouseEnter += (e) =>
        {
            IsHovered = true;
            e.Handled = true;
        };

        OnMouseLeave += (e) =>
        {
            IsHovered = false;
            e.Handled = true;
        };
    }

    public override List<string> GetInspectorProperties()
    {
        List<string> props = base.GetInspectorProperties();
        props.Add($"Text:     \"{Text}\"");
        props.Add($"Color:    #{TextColor}   Hover: #{HoverTextColor}");
        return props;
    }

    public override void Render(UIRenderer renderer)
    {
        int hoverState = !Enabled ? 0 : (IsHovered ? 2 : 1);

        TextureHandle texture = renderer.TextureManager.GetTextureId("/gui/gui.png");

        // Use fixed UV height of 20 to avoid reading into the next button in the spritesheet
        float uvHeight = 20;
        float vStart = 46 + hoverState * 20;

        renderer.DrawTexturedModalRect(texture, 0, 0, 0, vStart, ComputedWidth / 2, ComputedHeight, ComputedWidth / 2, uvHeight);
        renderer.DrawTexturedModalRect(texture, ComputedWidth / 2, 0, 200 - ComputedWidth / 2, vStart, ComputedWidth / 2, ComputedHeight, ComputedWidth / 2, uvHeight);

        Color tColor = !Enabled ? Color.GrayA0 : (IsHovered ? HoverTextColor : TextColor);
        float textY = (float)Math.Floor(ComputedHeight / 2.0) - 4;
        renderer.DrawScrollingCenteredText(Text, (int)ComputedWidth, (int)ComputedHeight, textY, tColor);

        base.Render(renderer);
    }
}
