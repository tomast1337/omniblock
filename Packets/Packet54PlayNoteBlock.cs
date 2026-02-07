using java.io;

namespace betareborn.Packets
{
    public class Packet54PlayNoteBlock : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet54PlayNoteBlock).TypeHandle);

        public int xLocation;
        public int yLocation;
        public int zLocation;
        public int instrumentType;
        public int pitch;

        public override void read(DataInputStream var1)
        {
            this.xLocation = var1.readInt();
            this.yLocation = var1.readShort();
            this.zLocation = var1.readInt();
            this.instrumentType = var1.read();
            this.pitch = var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.xLocation);
            var1.writeShort(this.yLocation);
            var1.writeInt(this.zLocation);
            var1.write(this.instrumentType);
            var1.write(this.pitch);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleNotePlay(this);
        }

        public override int size()
        {
            return 12;
        }
    }

}