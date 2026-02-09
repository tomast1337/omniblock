using java.io;

namespace betareborn.Network.Packets.Play
{
    public class ScreenHandlerAcknowledgementPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ScreenHandlerAcknowledgementPacket).TypeHandle);

        public int syncId;
        public short actionType;
        public bool accepted;

        public ScreenHandlerAcknowledgementPacket()
        {
        }

        public ScreenHandlerAcknowledgementPacket(int var1, short var2, bool var3)
        {
            syncId = var1;
            actionType = var2;
            accepted = var3;
        }

        public override void apply(NetHandler var1)
        {
            var1.onScreenHandlerAcknowledgement(this);
        }

        public override void read(DataInputStream var1)
        {
            syncId = (sbyte)var1.readByte();
            actionType = var1.readShort();
            accepted = (sbyte)var1.readByte() != 0;
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(syncId);
            var1.writeShort(actionType);
            var1.writeByte(accepted ? 1 : 0);
        }

        public override int size()
        {
            return 4;
        }
    }

}