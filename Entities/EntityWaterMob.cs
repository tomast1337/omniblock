using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityWaterMob : EntityCreature
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityWaterMob).TypeHandle);

        public EntityWaterMob(World var1) : base(var1)
        {
        }

        public override bool canBreatheUnderwater()
        {
            return true;
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
        }

        public override bool canSpawn()
        {
            return worldObj.checkIfAABBIsClear(boundingBox);
        }

        public override int getTalkInterval()
        {
            return 120;
        }
    }

}