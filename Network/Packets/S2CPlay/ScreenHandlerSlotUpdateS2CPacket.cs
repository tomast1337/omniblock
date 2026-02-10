using betareborn.Items;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ScreenHandlerSlotUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ScreenHandlerSlotUpdateS2CPacket).TypeHandle);

        public int syncId;
        public int slot;
        public ItemStack stack;

        public ScreenHandlerSlotUpdateS2CPacket()
        {
        }

        public ScreenHandlerSlotUpdateS2CPacket(int syncId, int slot, ItemStack stack)
        {
            this.syncId = syncId;
            this.slot = slot;
            this.stack = stack == null ? stack : stack.copy();
        }

        public override void apply(NetHandler var1)
        {
            var1.onScreenHandlerSlotUpdate(this);
        }

        public override void read(DataInputStream var1)
        {
            syncId = (sbyte)var1.readByte();
            slot = var1.readShort();
            short var2 = var1.readShort();
            if (var2 >= 0)
            {
                sbyte var3 = (sbyte)var1.readByte();
                short var4 = var1.readShort();
                stack = new ItemStack(var2, var3, var4);
            }
            else
            {
                stack = null;
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(syncId);
            var1.writeShort(slot);
            if (stack == null)
            {
                var1.writeShort(-1);
            }
            else
            {
                var1.writeShort(stack.itemId);
                var1.writeByte(stack.count);
                var1.writeShort(stack.getDamage());
            }

        }

        public override int size()
        {
            return 8;
        }
    }

}