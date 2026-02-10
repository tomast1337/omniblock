using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class WorldTimeUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(WorldTimeUpdateS2CPacket).TypeHandle);

        public long time;

        public WorldTimeUpdateS2CPacket()
        {
        }

        public WorldTimeUpdateS2CPacket(long time)
        {
            this.time = time;
        }

        public override void read(DataInputStream var1)
        {
            time = var1.readLong();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeLong(time);
        }

        public override void apply(NetHandler var1)
        {
            var1.onWorldTimeUpdate(this);
        }

        public override int size()
        {
            return 8;
        }
    }

}