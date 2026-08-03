using BetaSharp.Client.Entities;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.UI.Rendering;
using Color = BetaSharp.Client.UI.Colors.Color;

namespace BetaSharp.Client.UI.Controls.HUD;

public class Vignette(Func<ClientPlayerEntity?> getPlayer) : FullscreenElement
{
    private float _prevVignetteBrightness = 1.0f;

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);

        ClientPlayerEntity? player = getPlayer();
        if (player == null) return;

        float darkness = player.GetBrightnessAtEyes(partialTicks);
        darkness = 1.0f - darkness;
        if (darkness < 0.0f) darkness = 0.0f;
        if (darkness > 1.0f) darkness = 1.0f;

        _prevVignetteBrightness = (float)(_prevVignetteBrightness + (darkness - _prevVignetteBrightness) * 0.01D);
    }

    public override void Render(UIRenderer renderer)
    {
        renderer.SetAlphaTest(false);
        renderer.PushBlend(BlendMode.Darken);
        renderer.PushColor(new Color((byte)(255 * _prevVignetteBrightness), (byte)(255 * _prevVignetteBrightness), (byte)(255 * _prevVignetteBrightness)));

        renderer.DrawTexturedModalRect(renderer.TextureManager.GetTextureId("%blur%%clamp%/misc/vignette.png"), 0, 0, 0, 0, ComputedWidth, ComputedHeight, 256, 256, -90.0f);

        renderer.PopColor();
        renderer.PopBlend();
        renderer.SetAlphaTest(true);

        base.Render(renderer);
    }
}
