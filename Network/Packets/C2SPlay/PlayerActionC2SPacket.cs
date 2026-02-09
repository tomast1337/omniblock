using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class PlayerActionC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerActionC2SPacket).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int face;
        public int status;

        public PlayerActionC2SPacket()
        {
        }

        public PlayerActionC2SPacket(int var1, int var2, int var3, int var4, int var5)
        {
            status = var1;
            xPosition = var2;
            yPosition = var3;
            zPosition = var4;
            face = var5;
        }

        public override void read(DataInputStream var1)
        {
            status = var1.read();
            xPosition = var1.readInt();
            yPosition = var1.read();
            zPosition = var1.readInt();
            face = var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.write(status);
            var1.writeInt(xPosition);
            var1.write(yPosition);
            var1.writeInt(zPosition);
            var1.write(face);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleBlockDig(this);
        }

        public override int size()
        {
            return 11;
        }
    }

}