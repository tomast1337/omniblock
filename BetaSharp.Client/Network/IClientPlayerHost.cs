using BetaSharp.Client.Entities;
using BetaSharp.Client.Input;

namespace BetaSharp.Client.Network;

public interface IClientPlayerHost
{
    ClientPlayerEntity Player { get; }
    PlayerController PlayerController { set; }
    void Respawn(bool resetHealth, int dimensionId);
}
