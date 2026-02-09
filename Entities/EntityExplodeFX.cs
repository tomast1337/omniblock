using betareborn.Client.Rendering.Core;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityExplodeFX : EntityFX
    {

        public EntityExplodeFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : base(var1, var2, var4, var6, var8, var10, var12)
        {
            motionX = var8 + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.05F);
            motionY = var10 + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.05F);
            motionZ = var12 + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.05F);
            particleRed = particleGreen = particleBlue = rand.nextFloat() * 0.3F + 0.7F;
            particleScale = rand.nextFloat() * rand.nextFloat() * 6.0F + 1.0F;
            particleMaxAge = (int)(16.0D / ((double)rand.nextFloat() * 0.8D + 0.2D)) + 2;
        }

        public override void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            base.renderParticle(var1, var2, var3, var4, var5, var6, var7);
        }

        public override void onUpdate()
        {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            if (particleAge++ >= particleMaxAge)
            {
                markDead();
            }

            particleTextureIndex = 7 - particleAge * 8 / particleMaxAge;
            motionY += 0.004D;
            moveEntity(motionX, motionY, motionZ);
            motionX *= (double)0.9F;
            motionY *= (double)0.9F;
            motionZ *= (double)0.9F;
            if (onGround)
            {
                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
            }

        }
    }

}