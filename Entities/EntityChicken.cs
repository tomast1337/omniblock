using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Entities
{
    public class EntityChicken : EntityAnimal
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityChicken).TypeHandle);
        public bool field_753_a = false;
        public float field_752_b = 0.0F;
        public float destPos = 0.0F;
        public float field_757_d;
        public float field_756_e;
        public float field_755_h = 1.0F;
        public int timeUntilNextEgg;

        public EntityChicken(World var1) : base(var1)
        {
            texture = "/mob/chicken.png";
            setSize(0.3F, 0.4F);
            health = 4;
            timeUntilNextEgg = rand.nextInt(6000) + 6000;
        }

        public override void onLivingUpdate()
        {
            base.onLivingUpdate();
            field_756_e = field_752_b;
            field_757_d = destPos;
            destPos = (float)((double)destPos + (double)(onGround ? -1 : 4) * 0.3D);
            if (destPos < 0.0F)
            {
                destPos = 0.0F;
            }

            if (destPos > 1.0F)
            {
                destPos = 1.0F;
            }

            if (!onGround && field_755_h < 1.0F)
            {
                field_755_h = 1.0F;
            }

            field_755_h = (float)((double)field_755_h * 0.9D);
            if (!onGround && motionY < 0.0D)
            {
                motionY *= 0.6D;
            }

            field_752_b += field_755_h * 2.0F;
            if (!worldObj.multiplayerWorld && --timeUntilNextEgg <= 0)
            {
                worldObj.playSoundAtEntity(this, "mob.chickenplop", 1.0F, (rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F);
                dropItem(Item.egg.id, 1);
                timeUntilNextEgg = rand.nextInt(6000) + 6000;
            }

        }

        protected override void fall(float var1)
        {
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
            return "mob.chicken";
        }

        protected override string getHurtSound()
        {
            return "mob.chickenhurt";
        }

        protected override string getDeathSound()
        {
            return "mob.chickenhurt";
        }

        protected override int getDropItemId()
        {
            return Item.feather.id;
        }
    }

}