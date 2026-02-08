using betareborn.Blocks;
using betareborn.NBT;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Entities
{
    public abstract class EntityAnimal : EntityCreature
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityAnimal).TypeHandle);

        public EntityAnimal(World var1) : base(var1)
        {
        }

        protected override float getBlockPathWeight(int var1, int var2, int var3)
        {
            return worldObj.getBlockId(var1, var2 - 1, var3) == Block.GRASS_BLOCK.id ? 10.0F : worldObj.getLuminance(var1, var2, var3) - 0.5F;
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
            int var1 = MathHelper.floor_double(posX);
            int var2 = MathHelper.floor_double(boundingBox.minY);
            int var3 = MathHelper.floor_double(posZ);
            return worldObj.getBlockId(var1, var2 - 1, var3) == Block.GRASS_BLOCK.id && worldObj.getBrightness(var1, var2, var3) > 8 && base.canSpawn();
        }

        public override int getTalkInterval()
        {
            return 120;
        }
    }

}