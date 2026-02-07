using betareborn.Items;
using java.io;

namespace betareborn.Packets
{
    public class Packet15Place : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet15Place).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int direction;
        public ItemStack itemStack;

        public Packet15Place()
        {
        }

        public Packet15Place(int var1, int var2, int var3, int var4, ItemStack var5)
        {
            this.xPosition = var1;
            this.yPosition = var2;
            this.zPosition = var3;
            this.direction = var4;
            this.itemStack = var5;
        }

        public override void read(DataInputStream var1)
        {
            this.xPosition = var1.readInt();
            this.yPosition = var1.read();
            this.zPosition = var1.readInt();
            this.direction = var1.read();
            short var2 = var1.readShort();
            if (var2 >= 0)
            {
                sbyte var3 = (sbyte)var1.readByte();
                short var4 = var1.readShort();
                this.itemStack = new ItemStack(var2, var3, var4);
            }
            else
            {
                this.itemStack = null;
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.xPosition);
            var1.write(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.write(this.direction);
            if (this.itemStack == null)
            {
                var1.writeShort(-1);
            }
            else
            {
                var1.writeShort(this.itemStack.itemID);
                var1.writeByte(this.itemStack.count);
                var1.writeShort(this.itemStack.getItemDamage());
            }

        }

        public override void apply(NetHandler var1)
        {
            var1.handlePlace(this);
        }

        public override int size()
        {
            return 15;
        }
    }

}