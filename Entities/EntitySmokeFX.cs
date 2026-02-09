using betareborn.Client.Rendering.Core;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntitySmokeFX : EntityFX
    {
        float field_671_a;


        public EntitySmokeFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : this(var1, var2, var4, var6, var8, var10, var12, 1.0F)
        {
        }

        public EntitySmokeFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12, float var14) : base(var1, var2, var4, var6, 0.0D, 0.0D, 0.0D)
        {
            velocityX *= (double)0.1F;
            velocityY *= (double)0.1F;
            velocityZ *= (double)0.1F;
            velocityX += var8;
            velocityY += var10;
            velocityZ += var12;
            particleRed = particleGreen = particleBlue = (float)(java.lang.Math.random() * (double)0.3F);
            particleScale *= 12.0F / 16.0F;
            particleScale *= var14;
            field_671_a = particleScale;
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

            particleScale = field_671_a * var8;
            base.renderParticle(var1, var2, var3, var4, var5, var6, var7);
        }

        public override void onUpdate()
        {
            prevX = x;
            prevY = y;
            prevZ = z;
            if (particleAge++ >= particleMaxAge)
            {
                markDead();
            }

            particleTextureIndex = 7 - particleAge * 8 / particleMaxAge;
            velocityY += 0.004D;
            moveEntity(velocityX, velocityY, velocityZ);
            if (y == prevY)
            {
                velocityX *= 1.1D;
                velocityZ *= 1.1D;
            }

            velocityX *= (double)0.96F;
            velocityY *= (double)0.96F;
            velocityZ *= (double)0.96F;
            if (onGround)
            {
                velocityX *= (double)0.7F;
                velocityZ *= (double)0.7F;
            }

        }
    }

}