using betareborn.Blocks.Materials;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityBubbleFX : EntityFX
    {

        public EntityBubbleFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : base(var1, var2, var4, var6, var8, var10, var12)
        {
            particleRed = 1.0F;
            particleGreen = 1.0F;
            particleBlue = 1.0F;
            particleTextureIndex = 32;
            setSize(0.02F, 0.02F);
            particleScale *= rand.nextFloat() * 0.6F + 0.2F;
            motionX = var8 * (double)0.2F + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.02F);
            motionY = var10 * (double)0.2F + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.02F);
            motionZ = var12 * (double)0.2F + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.02F);
            particleMaxAge = (int)(8.0D / (java.lang.Math.random() * 0.8D + 0.2D));
        }

        public override void onUpdate()
        {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            motionY += 0.002D;
            moveEntity(motionX, motionY, motionZ);
            motionX *= (double)0.85F;
            motionY *= (double)0.85F;
            motionZ *= (double)0.85F;
            if (worldObj.getMaterial(MathHelper.floor_double(posX), MathHelper.floor_double(posY), MathHelper.floor_double(posZ)) != Material.WATER)
            {
                setEntityDead();
            }

            if (particleMaxAge-- <= 0)
            {
                setEntityDead();
            }

        }
    }

}