using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class PlayNoteSoundS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayNoteSoundS2CPacket).TypeHandle);

        public int xLocation;
        public int yLocation;
        public int zLocation;
        public int instrumentType;
        public int pitch;

        public PlayNoteSoundS2CPacket()
        {
        }

        public PlayNoteSoundS2CPacket(int x, int y, int z, int instrument, int pitch)
        {
            xLocation = x;
            yLocation = y;
            zLocation = z;
            instrumentType = instrument;
            this.pitch = pitch;
        }

        public override void read(DataInputStream var1)
        {
            xLocation = var1.readInt();
            yLocation = var1.readShort();
            zLocation = var1.readInt();
            instrumentType = var1.read();
            pitch = var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(xLocation);
            var1.writeShort(yLocation);
            var1.writeInt(zLocation);
            var1.write(instrumentType);
            var1.write(pitch);
        }

        public override void apply(NetHandler var1)
        {
            var1.onPlayNoteSound(this);
        }

        public override int size()
        {
            return 12;
        }
    }

}