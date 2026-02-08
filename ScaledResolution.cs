namespace betareborn
{
    public class ScaledResolution
    {
        private int scaledWidth;
        private int scaledHeight;
        public double field_25121_a;
        public double field_25120_b;
        public int scaleFactor;

        public ScaledResolution(GameOptions var1, int var2, int var3)
        {
            scaledWidth = var2;
            scaledHeight = var3;
            scaleFactor = 1;
            int var4 = var1.guiScale;
            if (var4 == 0)
            {
                var4 = 1000;
            }

            while (scaleFactor < var4 && scaledWidth / (scaleFactor + 1) >= 320 && scaledHeight / (scaleFactor + 1) >= 240)
            {
                ++scaleFactor;
            }

            field_25121_a = (double)scaledWidth / (double)scaleFactor;
            field_25120_b = (double)scaledHeight / (double)scaleFactor;
            scaledWidth = (int)java.lang.Math.ceil(field_25121_a);
            scaledHeight = (int)java.lang.Math.ceil(field_25120_b);
        }

        public int getScaledWidth()
        {
            return scaledWidth;
        }

        public int getScaledHeight()
        {
            return scaledHeight;
        }
    }

}