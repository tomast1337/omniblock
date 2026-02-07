using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityCow : EntityAnimal
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityCow).TypeHandle);

        public EntityCow(World var1) : base(var1)
        {
            this.texture = "/mob/cow.png";
            this.setSize(0.9F, 1.3F);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
        }

        protected override string getLivingSound()
        {
            return "mob.cow";
        }

        protected override string getHurtSound()
        {
            return "mob.cowhurt";
        }

        protected override string getDeathSound()
        {
            return "mob.cowhurt";
        }

        protected override float getSoundVolume()
        {
            return 0.4F;
        }

        protected override int getDropItemId()
        {
            return Item.leather.shiftedIndex;
        }

        public override bool interact(EntityPlayer var1)
        {
            ItemStack var2 = var1.inventory.getCurrentItem();
            if (var2 != null && var2.itemID == Item.bucketEmpty.shiftedIndex)
            {
                var1.inventory.setStack(var1.inventory.currentItem, new ItemStack(Item.bucketMilk));
                return true;
            }
            else
            {
                return false;
            }
        }
    }

}