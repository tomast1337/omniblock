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
    private Label _progress = null!;
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

        _progress = new Label
        {
            Text = "Waiting for spawn...",
            TextColor = Color.White,
            Centered = true
        };
        _progress.Style.MarginTop = 8;
        Root.AddChild(_progress);
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

        var preload = _networkHandler.Preload;
        _progress.Text = !preload.HasSpawn
            ? "Waiting for spawn..."
            : $"Terrain {preload.DecodedChunks}/{preload.RequiredChunks}  " +
              $"Meshes {preload.UploadedMeshes}/{preload.RequiredMeshes}";

        if (preload.IsReady)
        {
            Context.Navigator.Navigate(null);
        }
    }

    public override void KeyTyped(int key, char character)
    {
        // Do nothing to prevent escaping
    }
}
