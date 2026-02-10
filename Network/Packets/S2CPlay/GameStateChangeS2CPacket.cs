using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class GameStateChangeS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(GameStateChangeS2CPacket).TypeHandle);

        public static readonly string[] REASONS = new string[] { "tile.bed.notValid", null, null };
        public int reason;

        public GameStateChangeS2CPacket()
        {
        }

        public GameStateChangeS2CPacket(int reason)
        {
            this.reason = reason;
        }

        public override void read(DataInputStream var1)
        {
            reason = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(reason);
        }

        public override void apply(NetHandler var1)
        {
            var1.onGameStateChange(this);
        }

        public override int size()
        {
            return 1;
        }
    }

}