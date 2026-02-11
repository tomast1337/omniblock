namespace betareborn.Client.Colors
{
    public class FoliageColors : java.lang.Object
    {
        private static int[] foliageBuffer = new int[65536];

        public static void loadColors(int[] var0)
        {
            foliageBuffer = var0;
        }

        public static int getFoliageColor(double var0, double var2)
        {
            var2 *= var0;
            int var4 = (int)((1.0D - var0) * 255.0D);
            int var5 = (int)((1.0D - var2) * 255.0D);
            return foliageBuffer[var5 << 8 | var4];
        }

        public static int getSpruceColor()
        {
            return 6396257;
        }

        public static int getBirchColor()
        {
            return 8431445;
        }

        public static int getDefaultColor()
        {
            return 4764952;
        }
    }
}