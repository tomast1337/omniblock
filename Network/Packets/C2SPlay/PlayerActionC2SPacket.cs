using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class PlayerActionC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerActionC2SPacket).TypeHandle);

        public int x;
        public int y;
        public int z;
        public int direction;
        public int action;

        public PlayerActionC2SPacket()
        {
        }

        public PlayerActionC2SPacket(int var1, int var2, int var3, int var4, int var5)
        {
            action = var1;
            x = var2;
            y = var3;
            z = var4;
            direction = var5;
        }

        public override void read(DataInputStream var1)
        {
            action = var1.read();
            x = var1.readInt();
            y = var1.read();
            z = var1.readInt();
            direction = var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.write(action);
            var1.writeInt(x);
            var1.write(y);
            var1.writeInt(z);
            var1.write(direction);
        }

        public override void apply(NetHandler var1)
        {
            var1.handlePlayerAction(this);
        }

        public override int size()
        {
            return 11;
        }
    }

}