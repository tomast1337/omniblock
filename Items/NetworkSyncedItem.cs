using betareborn.Entities;
using betareborn.Packets;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class NetworkSyncedItem : Item
    {
        public NetworkSyncedItem(int i) : base(i)
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
