using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class MapUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(MapUpdateS2CPacket).TypeHandle);

        public short itemRawId;
        public short id;
        public byte[] updateData;

        public MapUpdateS2CPacket()
        {
            worldPacket = true;
        }

        public MapUpdateS2CPacket(short itemRawId, short id, byte[] updateData)
        {
            worldPacket = true;
            this.itemRawId = itemRawId;
            this.id = id;
            this.updateData = updateData;
        }

        public override void read(DataInputStream var1)
        {
            itemRawId = var1.readShort();
            id = var1.readShort();
            updateData = new byte[(sbyte)var1.readByte() & 255];
            var1.readFully(updateData);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeShort(itemRawId);
            var1.writeShort(id);
            var1.writeByte(updateData.Length);
            var1.write(updateData);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_28116_a(this);
        }

        public override int size()
        {
            return 4 + updateData.Length;
        }
    }

}