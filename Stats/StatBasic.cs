namespace betareborn.Stats
{
    public class StatBasic : StatBase
    {
        public StatBasic(int var1, string var2, IStatType var3) : base(var1, var2, var3)
        {
        }

        public StatBasic(int var1, string var2) : base(var1, var2)
        {
        }

        public override StatBase registerStat()
        {
            base.registerStat();
            Stats.field_25187_b.add(this);
            return this;
        }
    }

}