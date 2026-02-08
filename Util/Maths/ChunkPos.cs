using java.lang;

namespace betareborn.Util.Maths
{
    public readonly record struct ChunkPos
    {
        public readonly int x;
        public readonly int z;

        public ChunkPos(int var1, int var2)
        {
            x = var1;
            z = var2;
        }

        public static int chunkXZ2Int(int var0, int var1)
        {
            return (var0 < 0 ? Integer.MIN_VALUE : 0) | (var0 & Short.MAX_VALUE) << 16 | (var1 < 0 ? -Short.MIN_VALUE : 0) | var1 & Short.MAX_VALUE;
        }

        public override int GetHashCode()
        {
            return chunkXZ2Int(x, z);
        }

        public readonly bool Equals(ChunkPos var1)
        {
            return var1.x == x && var1.z == z;
        }
    }

}