using betareborn.Items;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class PlayerInteractBlockC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerInteractBlockC2SPacket).TypeHandle);

        public int x;
        public int y;
        public int z;
        public int side;
        public ItemStack stack;

        public PlayerInteractBlockC2SPacket()
        {
        }

        public PlayerInteractBlockC2SPacket(int var1, int var2, int var3, int var4, ItemStack var5)
        {
            x = var1;
            y = var2;
            z = var3;
            side = var4;
            stack = var5;
        }

        public override void read(DataInputStream var1)
        {
            x = var1.readInt();
            y = var1.read();
            z = var1.readInt();
            side = var1.read();
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
            var1.writeInt(x);
            var1.write(y);
            var1.writeInt(z);
            var1.write(side);
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

        public override void apply(NetHandler var1)
        {
            var1.onPlayerInteractBlock(this);
        }

        public override int size()
        {
            return 15;
        }
    }

}