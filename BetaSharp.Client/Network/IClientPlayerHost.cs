using OmniBlock.Client.Entities;
using OmniBlock.Client.Input;

namespace OmniBlock.Client.Network;

public interface IClientPlayerHost
{
    ClientPlayerEntity? Player { get; }
    void SetPlayerController(PlayerController controller);
    void Respawn(bool resetHealth, int dimensionId);
}
