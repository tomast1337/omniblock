using betareborn.Client.Rendering.Core;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntitySnowShovelFX : EntityFX
    {
        float field_27017_a;


        public EntitySnowShovelFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : this(var1, var2, var4, var6, var8, var10, var12, 1.0F)
        {
        }

        public EntitySnowShovelFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12, float var14) : base(var1, var2, var4, var6, var8, var10, var12)
        {
            motionX *= (double)0.1F;
            motionY *= (double)0.1F;
            motionZ *= (double)0.1F;
            motionX += var8;
            motionY += var10;
            motionZ += var12;
            particleRed = particleGreen = particleBlue = 1.0F - (float)(java.lang.Math.random() * (double)0.3F);
            particleScale *= 12.0F / 16.0F;
            particleScale *= var14;
            field_27017_a = particleScale;
            particleMaxAge = (int)(8.0D / (java.lang.Math.random() * 0.8D + 0.2D));
            particleMaxAge = (int)((float)particleMaxAge * var14);
            noClip = false;
        }

        public override void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            float var8 = ((float)particleAge + var2) / (float)particleMaxAge * 32.0F;
            if (var8 < 0.0F)
            {
                var8 = 0.0F;
            }

            if (var8 > 1.0F)
            {
                var8 = 1.0F;
            }

            particleScale = field_27017_a * var8;
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
            motionY -= 0.03D;
            moveEntity(motionX, motionY, motionZ);
            motionX *= (double)0.99F;
            motionY *= (double)0.99F;
            motionZ *= (double)0.99F;
            if (onGround)
            {
                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
            }

        }
    }

}