using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ScreenHandlerPropertyUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ScreenHandlerPropertyUpdateS2CPacket).TypeHandle);

        public int syncId;
        public int propertyId;
        public int value;

        public ScreenHandlerPropertyUpdateS2CPacket()
        {
        }

        public ScreenHandlerPropertyUpdateS2CPacket(int syncId, int propertyId, int value)
        {
            this.syncId = syncId;
            this.propertyId = propertyId;
            this.value = value;
        }

        public override void apply(NetHandler var1)
        {
            var1.onScreenHandlerPropertyUpdate(this);
        }

        public override void read(DataInputStream var1)
        {
            syncId = (sbyte)var1.readByte();
            propertyId = var1.readShort();
            value = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(syncId);
            var1.writeShort(propertyId);
            var1.writeShort(value);
        }

        public override int size()
        {
            return 5;
        }
    }

}