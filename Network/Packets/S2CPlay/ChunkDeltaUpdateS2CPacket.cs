using betareborn.Worlds;
using betareborn.Worlds.Chunks;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ChunkDeltaUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChunkDeltaUpdateS2CPacket).TypeHandle);

        public int x;
        public int z;
        public short[] positions;
        public byte[] blockRawIds;
        public byte[] blockMetadata;
        public int _size;

        public ChunkDeltaUpdateS2CPacket()
        {
            worldPacket = true;
        }

        public ChunkDeltaUpdateS2CPacket(int x, int z, short[] positions, int size, World world)
        {
            worldPacket = true;
            this.x = x;
            this.z = z;
            this._size = size;
            this.positions = new short[size];
            blockRawIds = new byte[size];
            blockMetadata = new byte[size];
            Chunk var6 = world.getChunk(x, z);

            for (int var7 = 0; var7 < size; var7++)
            {
                int var8 = positions[var7] >> 12 & 15;
                int var9 = positions[var7] >> 8 & 15;
                int var10 = positions[var7] & 255;
                this.positions[var7] = positions[var7];
                blockRawIds[var7] = (byte)var6.getBlockId(var8, var10, var9);
                blockMetadata[var7] = (byte)var6.getBlockMeta(var8, var10, var9);
            }
        }

        public override void read(DataInputStream var1)
        {
            x = var1.readInt();
            z = var1.readInt();
            _size = var1.readShort() & '\uffff';
            positions = new short[_size];

            blockRawIds = new byte[_size];
            blockMetadata = new byte[_size];

            for (int var2 = 0; var2 < _size; ++var2)
            {
                positions[var2] = var1.readShort();
            }

            var1.readFully(blockRawIds);
            var1.readFully(blockMetadata);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(x);
            var1.writeInt(z);
            var1.writeShort((short)_size);

            for (int var2 = 0; var2 < _size; ++var2)
            {
                var1.writeShort(positions[var2]);
            }

            var1.write(blockRawIds);
            var1.write(blockMetadata);
        }

        public override void apply(NetHandler var1)
        {
            var1.onChunkDeltaUpdate(this);
        }

        public override int size()
        {
            return 10 + _size * 4;
        }
    }

}