using betareborn.Blocks;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityFlying : EntityLiving
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityFlying).TypeHandle);

        public EntityFlying(World var1) : base(var1)
        {
        }

        protected override void fall(float var1)
        {
        }

        public override void moveEntityWithHeading(float var1, float var2)
        {
            if (isInWater())
            {
                moveFlying(var1, var2, 0.02F);
                moveEntity(motionX, motionY, motionZ);
                motionX *= (double)0.8F;
                motionY *= (double)0.8F;
                motionZ *= (double)0.8F;
            }
            else if (handleLavaMovement())
            {
                moveFlying(var1, var2, 0.02F);
                moveEntity(motionX, motionY, motionZ);
                motionX *= 0.5D;
                motionY *= 0.5D;
                motionZ *= 0.5D;
            }
            else
            {
                float var3 = 0.91F;
                if (onGround)
                {
                    var3 = 546.0F * 0.1F * 0.1F * 0.1F;
                    int var4 = worldObj.getBlockId(MathHelper.floor_double(posX), MathHelper.floor_double(boundingBox.minY) - 1, MathHelper.floor_double(posZ));
                    if (var4 > 0)
                    {
                        var3 = Block.BLOCKS[var4].slipperiness * 0.91F;
                    }
                }

                float var8 = 0.16277136F / (var3 * var3 * var3);
                moveFlying(var1, var2, onGround ? 0.1F * var8 : 0.02F);
                var3 = 0.91F;
                if (onGround)
                {
                    var3 = 546.0F * 0.1F * 0.1F * 0.1F;
                    int var5 = worldObj.getBlockId(MathHelper.floor_double(posX), MathHelper.floor_double(boundingBox.minY) - 1, MathHelper.floor_double(posZ));
                    if (var5 > 0)
                    {
                        var3 = Block.BLOCKS[var5].slipperiness * 0.91F;
                    }
                }

                moveEntity(motionX, motionY, motionZ);
                motionX *= (double)var3;
                motionY *= (double)var3;
                motionZ *= (double)var3;
            }

            field_705_Q = field_704_R;
            double var10 = posX - prevPosX;
            double var9 = posZ - prevPosZ;
            float var7 = MathHelper.sqrt_double(var10 * var10 + var9 * var9) * 4.0F;
            if (var7 > 1.0F)
            {
                var7 = 1.0F;
            }

            field_704_R += (var7 - field_704_R) * 0.4F;
            field_703_S += field_704_R;
        }

        public override bool isOnLadder()
        {
            return false;
        }
    }

}