namespace betareborn.Items
{
    public class ItemArmor : Item
    {

        private static readonly int[] damageReduceAmountArray = new int[] { 3, 8, 6, 3 };
        private static readonly int[] maxDamageArray = new int[] { 11, 16, 15, 13 };
        public readonly int armorLevel;
        public readonly int armorType;
        public readonly int damageReduceAmount;
        public readonly int renderIndex;

        public ItemArmor(int var1, int var2, int var3, int var4) : base(var1)
        {
            armorLevel = var2;
            armorType = var4;
            renderIndex = var3;
            damageReduceAmount = damageReduceAmountArray[var4];
            setMaxDamage(maxDamageArray[var4] * 3 << var2);
            maxCount = 1;
        }
    }

}