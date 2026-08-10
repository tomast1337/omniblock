using OmniBlock.Client.Network;
using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Network.Messages;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.Net;

public class DownloadingTerrainScreen(UIContext context, ClientNetworkHandler networkHandler) : UIScreen(context)
{
    private readonly ClientNetworkHandler _networkHandler = networkHandler;
    private int _tickCounter;

    public override bool PausesGame => false;

    protected override void Init()
    {
        Root.AddChild(new Background(BackgroundType.Dirt));
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;

        Label label = new()
        {
            Text = Translations.Get("multiplayer.downloadingTerrain"),
            TextColor = Color.White,
            Centered = true
        };
        Root.AddChild(label);
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);

        ++_tickCounter;
        if (_tickCounter % 20 == 0)
        {
            _networkHandler.SendMessage(new KeepAliveMessage());
        }

        _networkHandler?.Tick();
    }

    public override void KeyTyped(int key, char character)
    {
        // Do nothing to prevent escaping
    }
}
