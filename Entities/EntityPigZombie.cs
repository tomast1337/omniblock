using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityPigZombie : EntityZombie
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityPigZombie).TypeHandle);

        private int angerLevel = 0;
        private int randomSoundDelay = 0;
        private static readonly ItemStack defaultHeldItem = new ItemStack(Item.swordGold, 1);

        public EntityPigZombie(World var1) : base(var1)
        {
            texture = "/mob/pigzombie.png";
            moveSpeed = 0.5F;
            attackStrength = 5;
            isImmuneToFire = true;
        }

        public override void onUpdate()
        {
            moveSpeed = playerToAttack != null ? 0.95F : 0.5F;
            if (randomSoundDelay > 0 && --randomSoundDelay == 0)
            {
                worldObj.playSoundAtEntity(this, "mob.zombiepig.zpigangry", getSoundVolume() * 2.0F, ((rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F) * 1.8F);
            }

            base.onUpdate();
        }

        public override bool canSpawn()
        {
            return worldObj.difficultySetting > 0 && worldObj.checkIfAABBIsClear(boundingBox) && worldObj.getCollidingBoundingBoxes(this, boundingBox).Count == 0 && !worldObj.getIsAnyLiquid(boundingBox);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
            var1.setShort("Anger", (short)angerLevel);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
            angerLevel = var1.getShort("Anger");
        }

        protected override Entity findPlayerToAttack()
        {
            return angerLevel == 0 ? null : base.findPlayerToAttack();
        }

        public override void onLivingUpdate()
        {
            base.onLivingUpdate();
        }

        public override bool attackEntityFrom(Entity var1, int var2)
        {
            if (var1 is EntityPlayer)
            {
                var var3 = worldObj.getEntitiesWithinAABBExcludingEntity(this, boundingBox.expand(32.0D, 32.0D, 32.0D));

                for (int var4 = 0; var4 < var3.Count; ++var4)
                {
                    Entity var5 = var3[var4];
                    if (var5 is EntityPigZombie)
                    {
                        EntityPigZombie var6 = (EntityPigZombie)var5;
                        var6.becomeAngryAt(var1);
                    }
                }

                becomeAngryAt(var1);
            }

            return base.attackEntityFrom(var1, var2);
        }

        private void becomeAngryAt(Entity var1)
        {
            playerToAttack = var1;
            angerLevel = 400 + rand.nextInt(400);
            randomSoundDelay = rand.nextInt(40);
        }

        protected override String getLivingSound()
        {
            return "mob.zombiepig.zpig";
        }

        protected override String getHurtSound()
        {
            return "mob.zombiepig.zpighurt";
        }

        protected override String getDeathSound()
        {
            return "mob.zombiepig.zpigdeath";
        }

        protected override int getDropItemId()
        {
            return Item.porkCooked.id;
        }

        public override ItemStack getHeldItem()
        {
            return defaultHeldItem;
        }
    }

}