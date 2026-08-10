using OmniBlock.Client.Input;
using OmniBlock.Client.Network;
using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Screens.Menu.Net;

namespace OmniBlock.Client;

public partial class OmniBlock : IClientNetworkFactory
{
    public PlayerController CreatePlayerController(ClientNetworkHandler handler) =>
        new PlayerControllerMP(this, handler);

    public UIScreen CreateTerrainScreen(ClientNetworkHandler handler) =>
        new DownloadingTerrainScreen(UIContext, handler);

    public UIScreen CreateFailedScreen(string messageKey, string detailKey, object[]? args)
    {
        StopInternalServer();
        return new ConnectFailedScreen(UIContext, messageKey, detailKey, args);
    }
}
