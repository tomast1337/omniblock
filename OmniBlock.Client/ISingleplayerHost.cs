using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;

namespace OmniBlock.Client;

public interface ISingleplayerHost
{
    IWorldStorageSource SaveLoader { get; }
    void LoadWorld(string worldDir, string displayName, WorldSettings settings);
}
