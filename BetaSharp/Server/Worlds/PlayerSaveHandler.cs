using OmniBlock.Entities;

namespace OmniBlock.Server.Worlds;

public interface IPlayerStorage
{
    void SavePlayerData(EntityPlayer player);

    void LoadPlayerData(EntityPlayer player);
}
