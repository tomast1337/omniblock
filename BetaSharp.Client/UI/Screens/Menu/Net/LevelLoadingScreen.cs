using BetaSharp.Client.Guis;
using BetaSharp.Client.Network;
using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;
using BetaSharp.Network;
using BetaSharp.Network.Packets;
using BetaSharp.Server.Internal;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Client.UI.Screens.Menu.Net;

public class LevelLoadingScreen(BetaSharp game, string worldDir, WorldSettings settings) : UIScreen(game)
{
    private readonly ILogger<LevelLoadingScreen> _logger = Log.Instance.For<LevelLoadingScreen>();
    private readonly string _worldDir = worldDir;
    private readonly WorldSettings _settings = settings;
    private bool _serverStarted;

    private Label _lblProgress = null!;

    public override bool PausesGame => false;

    protected override void Init()
    {
        Root.AddChild(new Background());
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;

        Label lblTitle = new()
        {
            Text = "Loading level",
            TextColor = Color.White,
            Centered = true
        };
        lblTitle.Style.MarginBottom = 10;
        Root.AddChild(lblTitle);

        _lblProgress = new Label
        {
            Text = "Starting server...",
            TextColor = Color.White,
            Centered = true
        };
        Root.AddChild(_lblProgress);

        if (!_serverStarted)
        {
            _serverStarted = true;
            Game.StartInternalServer(_worldDir, _settings);
        }
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);

        if (Game.InternalServer != null)
        {
            if (Game.InternalServer.stopped)
            {
                Navigator.Navigate(new ConnectFailedScreen(Game, "connect.failed", "disconnect.genericReason", "Internal server stopped unexpectedly"));
                return;
            }

            string progressMsg = Game.InternalServer.progressMessage ?? "Starting server...";
            int progress = Game.InternalServer.progress;
            _lblProgress.Text = $"{progressMsg} ({progress}%)";

            if (Game.InternalServer.isReady)
            {
                InternalConnection clientConnection = new(null, "Internal-Client");
                InternalConnection serverConnection = new(null, "Internal-Server");

                clientConnection.AssignRemote(serverConnection);
                serverConnection.AssignRemote(clientConnection);

                Game.InternalServer.connections.AddInternalConnection(serverConnection);
                _logger.LogInformation("[Internal-Client] Created internal connection");

                ClientNetworkHandler clientHandler = new(Game, clientConnection);
                clientConnection.setNetworkHandler(clientHandler);
                _logger.LogInformation("[Internal-Client] Sending HandshakePacket");
                clientHandler.addToSendQueue(new HandshakePacket(Game.Session.username));

                Navigator.Navigate(new ConnectingScreen(Game, clientHandler));
            }
        }
    }

    public override void KeyTyped(int key, char character)
    {
        // Do nothing to prevent escaping
    }
}
