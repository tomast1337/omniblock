using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class IncreaseStatS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(IncreaseStatS2CPacket).TypeHandle);

        public int statId;
        public int amount;

        public IncreaseStatS2CPacket()
        {
        }

        public IncreaseStatS2CPacket(int statId, int amount)
        {
            this.statId = statId;
            this.amount = amount;
        }

        public override void apply(NetHandler var1)
        {
            var1.onIncreaseStat(this);
        }

        public override void read(DataInputStream var1)
        {
            statId = var1.readInt();
            amount = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(statId);
            var1.writeByte(amount);
        }

        public override int size()
        {
            return 6;
        }
    }

}