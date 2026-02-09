using betareborn.Entities;

namespace betareborn.Server.Worlds
{
    public interface PlayerSaveHandler
    {
        void savePlayerData(EntityPlayer player);

        void loadPlayerData(EntityPlayer player);
    }

}
