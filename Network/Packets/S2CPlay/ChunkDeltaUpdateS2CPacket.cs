using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ChunkDeltaUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChunkDeltaUpdateS2CPacket).TypeHandle);

        public int xPosition;
        public int zPosition;
        public short[] coordinateArray;
        public byte[] typeArray;
        public byte[] metadataArray;
        public int _size;

        public ChunkDeltaUpdateS2CPacket()
        {
            worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readInt();
            zPosition = var1.readInt();
            _size = var1.readShort() & '\uffff';
            coordinateArray = new short[_size];

            typeArray = new byte[_size];
            metadataArray = new byte[_size];

            for (int var2 = 0; var2 < _size; ++var2)
            {
                coordinateArray[var2] = var1.readShort();
            }

            var1.readFully(typeArray);
            var1.readFully(metadataArray);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(xPosition);
            var1.writeInt(zPosition);
            var1.writeShort((short)_size);

            for (int var2 = 0; var2 < _size; ++var2)
            {
                var1.writeShort(coordinateArray[var2]);
            }

            var1.write(typeArray);
            var1.write(metadataArray);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleMultiBlockChange(this);
        }

        public override int size()
        {
            return 10 + _size * 4;
        }
    }

}