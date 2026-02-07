using betareborn.Entities;

namespace betareborn.Items
{
    public class ItemSaddle : Item
    {

        public ItemSaddle(int var1) : base(var1)
        {
            maxStackSize = 1;
        }

        public override void saddleEntity(ItemStack var1, EntityLiving var2)
        {
            if (var2 is EntityPig)
            {
                EntityPig var3 = (EntityPig)var2;
                if (!var3.getSaddled())
                {
                    var3.setSaddled(true);
                    --var1.count;
                }
            }

        }

        public override bool hitEntity(ItemStack var1, EntityLiving var2, EntityLiving var3)
        {
            saddleEntity(var1, var2);
            return true;
        }
    }

}