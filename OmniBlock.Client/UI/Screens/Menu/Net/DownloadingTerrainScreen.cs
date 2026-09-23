using Microsoft.Extensions.Logging;
using OmniBlock.Client.Network;
using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Network.Messages;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.Net;

public class DownloadingTerrainScreen(UIContext context, ClientNetworkHandler networkHandler) : UIScreen(context)
{
    private readonly ILogger<DownloadingTerrainScreen> _logger = Log.Instance.For<DownloadingTerrainScreen>();
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

        TerrainLoadingMap loadingMap = new(_networkHandler.Preload);
        loadingMap.Style.Width = 80;
        loadingMap.Style.Height = 80;
        loadingMap.Style.MarginTop = 10;
        loadingMap.IsHitTestVisible = false;
        Root.AddChild(loadingMap);
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);

        ++_tickCounter;
        if (_tickCounter % 20 == 0)
        {
            _networkHandler.SendMessage(new KeepAliveMessage());
        }

        _networkHandler.Tick();

        var preload = _networkHandler.Preload;
        if (_tickCounter % 100 == 0)
        {
            _logger.LogInformation(
                "World preload progress: chunks {Decoded}/{RequiredChunks}, meshes {Meshes}/{RequiredMeshes}; missing mesh columns: {MissingMeshes}",
                preload.DecodedChunks,
                preload.RequiredChunks,
                preload.UploadedMeshes,
                preload.RequiredMeshes,
                preload.DescribeMissingMeshes());
        }

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
