using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class PlayerSpawnPositionS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerSpawnPositionS2CPacket).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            zPosition = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.writeInt(zPosition);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleSpawnPosition(this);
        }

        public override int size()
        {
            return 12;
        }
    }

}