using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class OpenScreenS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(OpenScreenS2CPacket).TypeHandle);

        public int syncId;
        public int screenHandlerId;
        public string name;
        public int slotsCount;

        public OpenScreenS2CPacket()
        {
        }

        public OpenScreenS2CPacket(int syncId, int screenHandlerId, String name, int size)
        {
            this.syncId = syncId;
            this.screenHandlerId = screenHandlerId;
            this.name = name;
            slotsCount = size;
        }

        public override void apply(NetHandler var1)
        {
            var1.onOpenScreen(this);
        }

        public override void read(DataInputStream var1)
        {
            syncId = (sbyte)var1.readByte();
            screenHandlerId = (sbyte)var1.readByte();
            name = var1.readUTF();
            slotsCount = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(syncId);
            var1.writeByte(screenHandlerId);
            var1.writeUTF(name);
            var1.writeByte(slotsCount);
        }

        public override int size()
        {
            return 3 + name.Length;
        }
    }

}