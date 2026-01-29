using System.Buffers;

namespace betareborn.Chunks
{
    public class ChunkSnapshot : IDisposable
    {
        private readonly byte[] blocks;
        private readonly NibbleArray data;
        private readonly NibbleArray skylightMap;
        private readonly NibbleArray blocklightMap;
        private bool disposed = false;
        private bool isLit = false;

        public ChunkSnapshot(Chunk toSnapshot)
        {
            blocks = ArrayPool<byte>.Shared.Rent(32768);
            Buffer.BlockCopy(toSnapshot.blocks, 0, blocks, 0, toSnapshot.blocks.Length);

            data = createNibbleArray(toSnapshot.data.data);
            skylightMap = createNibbleArray(toSnapshot.skylightMap.data);
            blocklightMap = createNibbleArray(toSnapshot.blocklightMap.data);
        }

        private static NibbleArray createNibbleArray(byte[] toCopy)
        {
            byte[] bytes = ArrayPool<byte>.Shared.Rent(toCopy.Length);
            Buffer.BlockCopy(toCopy, 0, bytes, 0, toCopy.Length);
            return new(bytes);
        }

        public int getBlockID(int x, int y, int z)
        {
            return blocks[x << 11 | z << 7 | y] & 255;
        }

        public int getBlockMetadata(int x, int y, int z)
        {
            return data.getNibble(x, y, z);
        }

        public int getSavedLightValue(EnumSkyBlock light, int x, int y, int z)
        {
            return light == EnumSkyBlock.Sky ? skylightMap.getNibble(x, y, z) : (light == EnumSkyBlock.Block ? blocklightMap.getNibble(x, y, z) : 0);
        }

        public int getBlockLightValue(int x, int y, int z, int var4, out bool isLit)
        {
            isLit = this.isLit;
            int var5 = skylightMap.getNibble(x, y, z);
            if (var5 > 0)
            {
                isLit = this.isLit = true;
            }

            var5 -= var4;
            int var6 = blocklightMap.getNibble(x, y, z);
            if (var6 > var5)
            {
                var5 = var6;
            }

            return var5;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            GC.SuppressFinalize(this);
            ArrayPool<byte>.Shared.Return(blocks);
            ArrayPool<byte>.Shared.Return(data.data);
            ArrayPool<byte>.Shared.Return(skylightMap.data);
            ArrayPool<byte>.Shared.Return(blocklightMap.data);
            disposed = true;
        }
    }
}
