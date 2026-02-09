using betareborn.Items;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class ClickSlotC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ClickSlotC2SPacket).TypeHandle);

        public int syncId;
        public int slot;
        public int button;
        public short actionType;
        public ItemStack stack;
        public bool holdingShift;

        public ClickSlotC2SPacket()
        {
        }

        public ClickSlotC2SPacket(int var1, int var2, int var3, bool var4, ItemStack var5, short var6)
        {
            syncId = var1;
            slot = var2;
            button = var3;
            stack = var5;
            actionType = var6;
            holdingShift = var4;
        }

        public override void apply(NetHandler var1)
        {
            var1.onClickSlot(this);
        }

        public override void read(DataInputStream var1)
        {
            syncId = (sbyte)var1.readByte();
            slot = var1.readShort();
            button = (sbyte)var1.readByte();
            actionType = var1.readShort();
            holdingShift = var1.readBoolean();
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
            var1.writeByte(button);
            var1.writeShort(actionType);
            var1.writeBoolean(holdingShift);
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
            return 11;
        }
    }

}