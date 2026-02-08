namespace betareborn.Stats
{
    public class DistanceStatFormatter : StatFormatter
    {

        public string format(int var1)
        {
            double var3 = (double)var1 / 100.0D;
            double var5 = var3 / 1000.0D;
            return var5 > 0.5D ? StatBase.defaultDecimalFormat().format(var5) + " km" : (var3 > 0.5D ? StatBase.defaultDecimalFormat().format(var3) + " m" : var1 + " cm");
        }
    }

}