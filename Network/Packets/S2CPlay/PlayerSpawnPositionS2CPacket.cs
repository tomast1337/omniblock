using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class PlayerSpawnPositionS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerSpawnPositionS2CPacket).TypeHandle);

        public int x;
        public int y;
        public int z;

        public PlayerSpawnPositionS2CPacket()
        {
        }

        public PlayerSpawnPositionS2CPacket(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override void read(DataInputStream var1)
        {
            x = var1.readInt();
            y = var1.readInt();
            z = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(x);
            var1.writeInt(y);
            var1.writeInt(z);
        }

        public override void apply(NetHandler var1)
        {
            var1.onPlayerSpawnPosition(this);
        }

        public override int size()
        {
            return 12;
        }
    }

}