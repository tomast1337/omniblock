using betareborn.Entities;
using betareborn.Network.Packets;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class NetworkSyncedItem : Item
    {
        public NetworkSyncedItem(int id) : base(id)
        {
        }

        public override bool isNetworkSynced()
        {
            return true;
        }

        public virtual Packet getUpdatePacket(ItemStack stack, World world, EntityPlayer player)
        {
            return null;
        }
    }
}
