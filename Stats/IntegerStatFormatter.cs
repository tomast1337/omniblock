namespace betareborn.Stats
{
    public class IntegerStatFormatter : StatFormatter
    {
        public string format(int var1)
        {
            return StatBase.defaultNumberFormat().format((long)var1);
        }
    }

}