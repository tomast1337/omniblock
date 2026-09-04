using Microsoft.Extensions.Logging;
using OmniBlock.Client.Network;
using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Network;
using OmniBlock.Network.Packets;
using OmniBlock.Worlds.Core.Systems;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.Net;

public class LevelLoadingScreen(
    UIContext context,
    ClientNetworkContext networkContext,
    string worldDir,
    WorldSettings settings,
    IInternalServerHost serverHost) : UIScreen(context)
{
    private readonly ILogger<LevelLoadingScreen> _logger = Log.Instance.For<LevelLoadingScreen>();

    private Label _lblProgress = null!;
    private bool _serverStarted;

    public override bool PausesGame => false;

    protected override void Init()
    {
        Root.AddChild(new Background());
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;

        Label lblTitle = new()
        {
            Text = Translations.Get("loading.loadingLevel"),
            TextColor = Color.White,
            Centered = true
        };
        lblTitle.Style.MarginBottom = 10;
        Root.AddChild(lblTitle);

        _lblProgress = new Label
        {
            Text = Translations.Get("loading.startingServer"),
            TextColor = Color.White,
            Centered = true
        };
        Root.AddChild(_lblProgress);

        if (!_serverStarted)
        {
            _serverStarted = true;
            serverHost.StartInternalServer(worldDir, settings);
        }
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);

        var server = serverHost.InternalServer;
        if (server != null)
        {
            if (server.stopped)
            {
                Context.Navigator.Navigate(new ConnectFailedScreen(Context, "connect.failed", "disconnect.genericReason", "Internal server stopped unexpectedly"));
                return;
            }

            var progressMsg = server.progressMessage ?? Translations.Get("loading.startingServer");
            var progress = server.progress;
            _lblProgress.Text = $"{progressMsg} ({progress}%)";

            if (server.isReady)
            {
                InternalConnection clientConnection = new(null, "Internal-Client");
                InternalConnection serverConnection = new(null, "Internal-Server");

                clientConnection.AssignRemote(serverConnection);
                serverConnection.AssignRemote(clientConnection);

                server.connections.AddInternalConnection(serverConnection);
                _logger.LogInformation("[Internal-Client] Created internal connection");

                ClientNetworkHandler clientHandler = new(networkContext, clientConnection);
                clientConnection.setNetworkHandler(clientHandler);
                _logger.LogInformation("[Internal-Client] Sending HandshakePacket");
                clientHandler.AddToSendQueue(HandshakePacket.Get(networkContext.Session.username));

                Context.Navigator.Navigate(new ConnectingScreen(Context, clientHandler));
            }
        }
    }

    public override void KeyTyped(int key, char character)
    {
        // Do nothing to prevent escaping
    }
}
