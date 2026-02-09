using betareborn.Client.Rendering.Core;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityPortalFX : EntityFX
    {

        private float field_4083_a;
        private double field_4086_p;
        private double field_4085_q;
        private double field_4084_r;

        public EntityPortalFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : base(var1, var2, var4, var6, var8, var10, var12)
        {
            motionX = var8;
            motionY = var10;
            motionZ = var12;
            field_4086_p = posX = var2;
            field_4085_q = posY = var4;
            field_4084_r = posZ = var6;
            float var14 = rand.nextFloat() * 0.6F + 0.4F;
            field_4083_a = particleScale = rand.nextFloat() * 0.2F + 0.5F;
            particleRed = particleGreen = particleBlue = 1.0F * var14;
            particleGreen *= 0.3F;
            particleRed *= 0.9F;
            particleMaxAge = (int)(java.lang.Math.random() * 10.0D) + 40;
            noClip = true;
            particleTextureIndex = (int)(java.lang.Math.random() * 8.0D);
        }

        public override void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            float var8 = ((float)particleAge + var2) / (float)particleMaxAge;
            var8 = 1.0F - var8;
            var8 *= var8;
            var8 = 1.0F - var8;
            particleScale = field_4083_a * var8;
            base.renderParticle(var1, var2, var3, var4, var5, var6, var7);
        }

        public override float getEntityBrightness(float var1)
        {
            float var2 = base.getEntityBrightness(var1);
            float var3 = (float)particleAge / (float)particleMaxAge;
            var3 *= var3;
            var3 *= var3;
            return var2 * (1.0F - var3) + var3;
        }

        public override void onUpdate()
        {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            float var1 = (float)particleAge / (float)particleMaxAge;
            float var2 = var1;
            var1 = -var1 + var1 * var1 * 2.0F;
            var1 = 1.0F - var1;
            posX = field_4086_p + motionX * (double)var1;
            posY = field_4085_q + motionY * (double)var1 + (double)(1.0F - var2);
            posZ = field_4084_r + motionZ * (double)var1;
            if (particleAge++ >= particleMaxAge)
            {
                markDead();
            }

        }
    }

}