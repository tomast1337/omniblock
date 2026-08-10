using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.HUD;

public class ControlTooltipBar : UIElement
{
    private const int IconSize = 16;
    private const int TextVerticalOffset = 4;
    private const int Spacing = 10;
    private readonly IControllerState _controllerState;
    private readonly Func<InGameTipContext?>? _inGameSource;
    private readonly GameOptions _options;
    private readonly UIScreen? _screen;
    private readonly TextRenderer _textRenderer;
    private readonly List<ActionTip> _tips = [];

    public ControlTooltipBar(UIContext context, UIScreen screen)
    {
        _controllerState = context.ControllerState;
        _options = context.Options;
        _textRenderer = context.TextRenderer;
        _screen = screen;
        IsHitTestVisible = false;
        Style.Height = IconSize;
    }

    public ControlTooltipBar(UIContext context, Func<InGameTipContext?> inGameSource)
    {
        _controllerState = context.ControllerState;
        _options = context.Options;
        _textRenderer = context.TextRenderer;
        _inGameSource = inGameSource;
        _screen = null;
        IsHitTestVisible = false;
        Style.Height = IconSize;
    }

    public override void Render(UIRenderer renderer)
    {
        _tips.Clear();

        if (!_controllerState.IsControllerMode || _options.HideGUI)
        {
            base.Render(renderer);
            return;
        }

        if (_screen == null)
        {
            InGameTipContext? ctx = _inGameSource?.Invoke();
            if (ctx == null)
            {
                base.Render(renderer);
                return;
            }

            ControlTooltip.PopulateInGameTips(ctx, _tips);
        }
        else
        {
            ControlTooltip.PopulateGuiTips(_screen, _tips);
        }

        if (_tips.Count == 0)
        {
            base.Render(renderer);
            return;
        }

        float x = 0;
        foreach (ActionTip tip in _tips)
        {
            string? assetPath = ControlTooltip.GetAssetPath(tip.Icon);
            if (assetPath != null)
            {
                TextureHandle texture = renderer.TextureManager.GetTextureId(assetPath);
                renderer.DrawTexture(texture, x, 0, IconSize, IconSize);
                x += IconSize + 4;
            }

            renderer.DrawText(tip.Action, x, TextVerticalOffset, Color.White);
            x += _textRenderer.GetStringWidth(tip.Action) + Spacing;
        }

        base.Render(renderer);
    }
}
