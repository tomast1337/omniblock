using OmniBlock.Client.Input;
using OmniBlock.Client.UI;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Network;

public interface IClientNetworkFactory
{
    int MaximumTerrainLodSpatialLevel =>
        TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel;

    PlayerController CreatePlayerController(ClientNetworkHandler handler);
    UIScreen CreateTerrainScreen(ClientNetworkHandler handler);
    UIScreen CreateFailedScreen(string messageKey, string detailKey, object[]? args);
}
