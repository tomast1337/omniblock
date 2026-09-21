using OmniBlock.Client.Input;
using OmniBlock.Client.Network;
using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Screens.Menu.Net;

namespace OmniBlock.Client;

public partial class OmniBlock : IClientNetworkFactory
{
    // This is the immutable session capability, not the currently selected horizon. A user may
    // lower the horizon while L6 responses are in flight; decode must continue accepting every
    // level negotiated for the session.
    public int MaximumTerrainLodSpatialLevel => TerrainLodPolicy.MaximumSpatialLevel;

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
