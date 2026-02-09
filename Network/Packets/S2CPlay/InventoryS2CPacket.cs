using betareborn.Items;
using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class InventoryS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(InventoryS2CPacket).TypeHandle);

        public int windowId;
        public ItemStack[] itemStack;

        public override void read(DataInputStream var1)
        {
            windowId = (sbyte)var1.readByte();
            short var2 = var1.readShort();
            itemStack = new ItemStack[var2];

            for (int var3 = 0; var3 < var2; ++var3)
            {
                short var4 = var1.readShort();
                if (var4 >= 0)
                {
                    sbyte var5 = (sbyte)var1.readByte();
                    short var6 = var1.readShort();

                    itemStack[var3] = new ItemStack(var4, var5, var6);
                }
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(windowId);
            var1.writeShort(itemStack.Length);

            for (int var2 = 0; var2 < itemStack.Length; ++var2)
            {
                if (itemStack[var2] == null)
                {
                    var1.writeShort(-1);
                }
                else
                {
                    var1.writeShort((short)itemStack[var2].itemID);
                    var1.writeByte((byte)itemStack[var2].count);
                    var1.writeShort((short)itemStack[var2].getDamage());
                }
            }

        }

        public override void apply(NetHandler var1)
        {
            var1.func_20094_a(this);
        }

        public override int size()
        {
            return 3 + itemStack.Length * 5;
        }
    }

}