using betareborn.Items;
using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class PlayerInteractBlockC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerInteractBlockC2SPacket).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int direction;
        public ItemStack itemStack;

        public PlayerInteractBlockC2SPacket()
        {
        }

        public PlayerInteractBlockC2SPacket(int var1, int var2, int var3, int var4, ItemStack var5)
        {
            xPosition = var1;
            yPosition = var2;
            zPosition = var3;
            direction = var4;
            itemStack = var5;
        }

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readInt();
            yPosition = var1.read();
            zPosition = var1.readInt();
            direction = var1.read();
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
            var1.writeInt(xPosition);
            var1.write(yPosition);
            var1.writeInt(zPosition);
            var1.write(direction);
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