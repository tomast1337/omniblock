using betareborn.Items;
using java.io;
using java.util;

namespace betareborn.Network.Packets.S2CPlay
{
    public class InventoryS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(InventoryS2CPacket).TypeHandle);

        public int syncId;
        public ItemStack[] contents;

        public InventoryS2CPacket()
        {
        }

        public InventoryS2CPacket(int syncId, List contents)
        {
            this.syncId = syncId;
            this.contents = new ItemStack[contents.size()];

            for (int var3 = 0; var3 < this.contents.Length; var3++)
            {
                ItemStack var4 = (ItemStack)contents.get(var3);
                this.contents[var3] = var4 == null ? null : var4.copy();
            }
        }

        public override void read(DataInputStream var1)
        {
            syncId = (sbyte)var1.readByte();
            short var2 = var1.readShort();
            contents = new ItemStack[var2];

            for (int var3 = 0; var3 < var2; ++var3)
            {
                short var4 = var1.readShort();
                if (var4 >= 0)
                {
                    sbyte var5 = (sbyte)var1.readByte();
                    short var6 = var1.readShort();

                    contents[var3] = new ItemStack(var4, var5, var6);
                }
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(syncId);
            var1.writeShort(contents.Length);

            for (int var2 = 0; var2 < contents.Length; ++var2)
            {
                if (contents[var2] == null)
                {
                    var1.writeShort(-1);
                }
                else
                {
                    var1.writeShort((short)contents[var2].itemId);
                    var1.writeByte((byte)contents[var2].count);
                    var1.writeShort((short)contents[var2].getDamage());
                }
            }

        }

        public override void apply(NetHandler var1)
        {
            var1.onInventory(this);
        }

        public override int size()
        {
            return 3 + contents.Length * 5;
        }
    }

}