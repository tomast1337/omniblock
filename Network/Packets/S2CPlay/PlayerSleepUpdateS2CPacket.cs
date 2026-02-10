using betareborn.Entities;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class PlayerSleepUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerSleepUpdateS2CPacket).TypeHandle);

        public int id;
        public int x;
        public int y;
        public int z;
        public int status;

        public PlayerSleepUpdateS2CPacket()
        {
        }

        public PlayerSleepUpdateS2CPacket(Entity player, int status, int x, int y, int z)
        {
            this.status = status;
            this.x = x;
            this.y = y;
            this.z = z;
            this.id = player.id;
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
            status = (sbyte)var1.readByte();
            x = var1.readInt();
            y = (sbyte)var1.readByte();
            z = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
            var1.writeByte(status);
            var1.writeInt(x);
            var1.writeByte(y);
            var1.writeInt(z);
        }

        public override void apply(NetHandler var1)
        {
            var1.onPlayerSleepUpdate(this);
        }

        public override int size()
        {
            return 14;
        }
    }

}