using betareborn.Items;
using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class ClickSlotC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ClickSlotC2SPacket).TypeHandle);

        public int window_Id;
        public int inventorySlot;
        public int mouseClick;
        public short action;
        public ItemStack itemStack;
        public bool field_27050_f;

        public ClickSlotC2SPacket()
        {
        }

        public ClickSlotC2SPacket(int var1, int var2, int var3, bool var4, ItemStack var5, short var6)
        {
            window_Id = var1;
            inventorySlot = var2;
            mouseClick = var3;
            itemStack = var5;
            action = var6;
            field_27050_f = var4;
        }

        public override void apply(NetHandler var1)
        {
            var1.func_20091_a(this);
        }

        public override void read(DataInputStream var1)
        {
            window_Id = (sbyte)var1.readByte();
            inventorySlot = var1.readShort();
            mouseClick = (sbyte)var1.readByte();
            action = var1.readShort();
            field_27050_f = var1.readBoolean();
            short var2 = var1.readShort();
            if (var2 >= 0)
            {
                sbyte var3 = (sbyte)var1.readByte();
                short var4 = var1.readShort();
                itemStack = new ItemStack(var2, var3, var4);
            }
            else
            {
                itemStack = null;
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(window_Id);
            var1.writeShort(inventorySlot);
            var1.writeByte(mouseClick);
            var1.writeShort(action);
            var1.writeBoolean(field_27050_f);
            if (itemStack == null)
            {
                var1.writeShort(-1);
            }
            else
            {
                var1.writeShort(itemStack.itemID);
                var1.writeByte(itemStack.count);
                var1.writeShort(itemStack.getDamage());
            }

        }

        public override int size()
        {
            return 11;
        }
    }

}