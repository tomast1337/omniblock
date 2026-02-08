namespace betareborn.Util.Maths
{
    public class BlockPos : java.lang.Object
    {
        public readonly int x;
        public readonly int y;
        public readonly int z;

        public BlockPos(int var1, int var2, int var3)
        {
            x = var1;
            y = var2;
            z = var3;
        }

        public override bool equals(object var1)
        {
            if (var1 is not BlockPos)
            {
                return false;
            }
            else
            {
                BlockPos var2 = (BlockPos)var1;
                return var2.x == x && var2.y == y && var2.z == z;
            }
        }

        public override int hashCode()
        {
            return x * 8976890 + y * 981131 + z;
        }
    }

}