using betareborn.Network;
using java.io;

namespace betareborn.Packets
{
    public class Packet52MultiBlockChange : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet52MultiBlockChange).TypeHandle);

        public int xPosition;
        public int zPosition;
        public short[] coordinateArray;
        public byte[] typeArray;
        public byte[] metadataArray;
        public int _size;

        public Packet52MultiBlockChange()
        {
            this.worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            this.xPosition = var1.readInt();
            this.zPosition = var1.readInt();
            this._size = var1.readShort() & '\uffff';
            this.coordinateArray = new short[this._size];

            this.typeArray = new byte[this._size];
            this.metadataArray = new byte[this._size];

            for (int var2 = 0; var2 < this._size; ++var2)
            {
                this.coordinateArray[var2] = var1.readShort();
            }

            var1.readFully(this.typeArray);
            var1.readFully(this.metadataArray);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.xPosition);
            var1.writeInt(this.zPosition);
            var1.writeShort((short)this._size);

            for (int var2 = 0; var2 < this._size; ++var2)
            {
                var1.writeShort(this.coordinateArray[var2]);
            }

            var1.write(this.typeArray);
            var1.write(this.metadataArray);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleMultiBlockChange(this);
        }

        public override int size()
        {
            return 10 + this._size * 4;
        }
    }

}