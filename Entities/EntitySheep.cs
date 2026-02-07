using betareborn.Blocks;
using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Entities
{
    public class EntitySheep : EntityAnimal
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntitySheep).TypeHandle);
        public static readonly float[][] fleeceColorTable = [[1.0F, 1.0F, 1.0F], [0.95F, 0.7F, 0.2F], [0.9F, 0.5F, 0.85F], [0.6F, 0.7F, 0.95F], [0.9F, 0.9F, 0.2F], [0.5F, 0.8F, 0.1F], [0.95F, 0.7F, 0.8F], [0.3F, 0.3F, 0.3F], [0.6F, 0.6F, 0.6F], [0.3F, 0.6F, 0.7F], [0.7F, 0.4F, 0.9F], [0.2F, 0.4F, 0.8F], [0.5F, 0.4F, 0.3F], [0.4F, 0.5F, 0.2F], [0.8F, 0.3F, 0.3F], [0.1F, 0.1F, 0.1F]];

        public EntitySheep(World var1) : base(var1)
        {
            texture = "/mob/sheep.png";
            setSize(0.9F, 1.3F);
        }

        protected override void entityInit()
        {
            base.entityInit();
            dataWatcher.addObject(16, new java.lang.Byte((byte)0));
        }

        public override bool attackEntityFrom(Entity var1, int var2)
        {
            return base.attackEntityFrom(var1, var2);
        }

        protected override void dropFewItems()
        {
            if (!getSheared())
            {
                entityDropItem(new ItemStack(Block.WOOL.id, 1, getFleeceColor()), 0.0F);
            }

        }

        protected override int getDropItemId()
        {
            return Block.WOOL.id;
        }

        public override bool interact(EntityPlayer var1)
        {
            ItemStack var2 = var1.inventory.getCurrentItem();
            if (var2 != null && var2.itemID == Item.shears.id && !getSheared())
            {
                if (!worldObj.multiplayerWorld)
                {
                    setSheared(true);
                    int var3 = 2 + rand.nextInt(3);

                    for (int var4 = 0; var4 < var3; ++var4)
                    {
                        EntityItem var5 = entityDropItem(new ItemStack(Block.WOOL.id, 1, getFleeceColor()), 1.0F);
                        var5.motionY += (double)(rand.nextFloat() * 0.05F);
                        var5.motionX += (double)((rand.nextFloat() - rand.nextFloat()) * 0.1F);
                        var5.motionZ += (double)((rand.nextFloat() - rand.nextFloat()) * 0.1F);
                    }
                }

                var2.damageItem(1, var1);
            }

            return false;
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
            var1.setBoolean("Sheared", getSheared());
            var1.setByte("Color", (sbyte)getFleeceColor());
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
            setSheared(var1.getBoolean("Sheared"));
            setFleeceColor(var1.getByte("Color"));
        }

        protected override string getLivingSound()
        {
            return "mob.sheep";
        }

        protected override string getHurtSound()
        {
            return "mob.sheep";
        }

        protected override string getDeathSound()
        {
            return "mob.sheep";
        }

        public int getFleeceColor()
        {
            return dataWatcher.getWatchableObjectByte(16) & 15;
        }

        public void setFleeceColor(int var1)
        {
            sbyte var2 = dataWatcher.getWatchableObjectByte(16);
            dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 & 240 | var1 & 15)));
        }

        public bool getSheared()
        {
            return (dataWatcher.getWatchableObjectByte(16) & 16) != 0;
        }

        public void setSheared(bool var1)
        {
            sbyte var2 = dataWatcher.getWatchableObjectByte(16);
            if (var1)
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 | 16)));
            }
            else
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 & -17)));
            }

        }

        public static int getRandomFleeceColor(java.util.Random var0)
        {
            int var1 = var0.nextInt(100);
            return var1 < 5 ? 15 : (var1 < 10 ? 7 : (var1 < 15 ? 8 : (var1 < 18 ? 12 : (var0.nextInt(500) == 0 ? 6 : 0))));
        }
    }

}