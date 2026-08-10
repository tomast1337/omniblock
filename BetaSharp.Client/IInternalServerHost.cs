using OmniBlock.Server.Internal;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client;

public interface IInternalServerHost
{
    InternalServer? InternalServer { get; }
    void StartInternalServer(string worldDir, WorldSettings settings);
}
