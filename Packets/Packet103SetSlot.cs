using betareborn.Items;
using java.io;

namespace betareborn.Packets
{
    public class Packet103SetSlot : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet103SetSlot).TypeHandle);

        public int windowId;
        public int itemSlot;
        public ItemStack myItemStack;

        public override void apply(NetHandler var1)
        {
            var1.func_20088_a(this);
        }

        public override void read(DataInputStream var1)
        {
            this.windowId = (sbyte)var1.readByte();
            this.itemSlot = var1.readShort();
            short var2 = var1.readShort();
            if (var2 >= 0)
            {
                sbyte var3 = (sbyte)var1.readByte();
                short var4 = var1.readShort();
                this.myItemStack = new ItemStack(var2, var3, var4);
            }
            else
            {
                this.myItemStack = null;
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(this.windowId);
            var1.writeShort(this.itemSlot);
            if (this.myItemStack == null)
            {
                var1.writeShort(-1);
            }
            else
            {
                var1.writeShort(this.myItemStack.itemID);
                var1.writeByte(this.myItemStack.count);
                var1.writeShort(this.myItemStack.getItemDamage());
            }

        }

        public override int size()
        {
            return 8;
        }
    }

}