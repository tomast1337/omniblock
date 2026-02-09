using betareborn.Items;
using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ScreenHandlerSlotUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ScreenHandlerSlotUpdateS2CPacket).TypeHandle);

        public int windowId;
        public int itemSlot;
        public ItemStack myItemStack;

        public override void apply(NetHandler var1)
        {
            var1.func_20088_a(this);
        }

        public override void read(DataInputStream var1)
        {
            windowId = (sbyte)var1.readByte();
            itemSlot = var1.readShort();
            short var2 = var1.readShort();
            if (var2 >= 0)
            {
                sbyte var3 = (sbyte)var1.readByte();
                short var4 = var1.readShort();
                myItemStack = new ItemStack(var2, var3, var4);
            }
            else
            {
                myItemStack = null;
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(windowId);
            var1.writeShort(itemSlot);
            if (myItemStack == null)
            {
                var1.writeShort(-1);
            }
            else
            {
                var1.writeShort(myItemStack.itemID);
                var1.writeByte(myItemStack.count);
                var1.writeShort(myItemStack.getDamage());
            }

        }

        public override int size()
        {
            return 8;
        }
    }

}