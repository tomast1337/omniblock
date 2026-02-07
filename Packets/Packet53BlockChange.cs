using java.io;

namespace betareborn.Packets
{
    public class Packet53BlockChange : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet53BlockChange).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int type;
        public int metadata;

        public Packet53BlockChange()
        {
            this.worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            this.xPosition = var1.readInt();
            this.yPosition = var1.read();
            this.zPosition = var1.readInt();
            this.type = var1.read();
            this.metadata = var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.xPosition);
            var1.write(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.write(this.type);
            var1.write(this.metadata);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleBlockChange(this);
        }

        public override int size()
        {
            return 11;
        }
    }

}