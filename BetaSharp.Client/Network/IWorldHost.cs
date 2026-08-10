using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Network;

public interface IWorldHost
{
    World? World { get; }
    void ChangeWorld(World? world);
}
