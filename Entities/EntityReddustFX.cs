using betareborn.Client.Rendering.Core;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityReddustFX : EntityFX
    {
        float field_673_a;


        public EntityReddustFX(World var1, double var2, double var4, double var6, float var8, float var9, float var10) : this(var1, var2, var4, var6, 1.0F, var8, var9, var10)
        {
        }

        public EntityReddustFX(World var1, double var2, double var4, double var6, float var8, float var9, float var10, float var11) : base(var1, var2, var4, var6, 0.0D, 0.0D, 0.0D)
        {
            motionX *= (double)0.1F;
            motionY *= (double)0.1F;
            motionZ *= (double)0.1F;
            if (var9 == 0.0F)
            {
                var9 = 1.0F;
            }

            float var12 = (float)java.lang.Math.random() * 0.4F + 0.6F;
            particleRed = ((float)(java.lang.Math.random() * (double)0.2F) + 0.8F) * var9 * var12;
            particleGreen = ((float)(java.lang.Math.random() * (double)0.2F) + 0.8F) * var10 * var12;
            particleBlue = ((float)(java.lang.Math.random() * (double)0.2F) + 0.8F) * var11 * var12;
            particleScale *= 12.0F / 16.0F;
            particleScale *= var8;
            field_673_a = particleScale;
            particleMaxAge = (int)(8.0D / (java.lang.Math.random() * 0.8D + 0.2D));
            particleMaxAge = (int)((float)particleMaxAge * var8);
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

            particleScale = field_673_a * var8;
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
            moveEntity(motionX, motionY, motionZ);
            if (posY == prevPosY)
            {
                motionX *= 1.1D;
                motionZ *= 1.1D;
            }

            motionX *= (double)0.96F;
            motionY *= (double)0.96F;
            motionZ *= (double)0.96F;
            if (onGround)
            {
                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
            }

        }
    }

}