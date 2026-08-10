using OmniBlock.Client.Input;
using OmniBlock.Client.UI;

namespace OmniBlock.Client.Network;

public interface IClientNetworkFactory
{
    PlayerController CreatePlayerController(ClientNetworkHandler handler);
    UIScreen CreateTerrainScreen(ClientNetworkHandler handler);
    UIScreen CreateFailedScreen(string messageKey, string detailKey, object[]? args);
}
