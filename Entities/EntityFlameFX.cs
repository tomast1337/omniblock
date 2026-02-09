using betareborn.Client.Rendering.Core;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityFlameFX : EntityFX
    {

        private float field_672_a;

        public EntityFlameFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : base(var1, var2, var4, var6, var8, var10, var12)
        {
            motionX = motionX * (double)0.01F + var8;
            motionY = motionY * (double)0.01F + var10;
            motionZ = motionZ * (double)0.01F + var12;
            double var10000 = var2 + (double)((rand.nextFloat() - rand.nextFloat()) * 0.05F);
            var10000 = var4 + (double)((rand.nextFloat() - rand.nextFloat()) * 0.05F);
            var10000 = var6 + (double)((rand.nextFloat() - rand.nextFloat()) * 0.05F);
            field_672_a = particleScale;
            particleRed = particleGreen = particleBlue = 1.0F;
            particleMaxAge = (int)(8.0D / (java.lang.Math.random() * 0.8D + 0.2D)) + 4;
            noClip = true;
            particleTextureIndex = 48;
        }

        public override void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            float var8 = ((float)particleAge + var2) / (float)particleMaxAge;
            particleScale = field_672_a * (1.0F - var8 * var8 * 0.5F);
            base.renderParticle(var1, var2, var3, var4, var5, var6, var7);
        }

        public override float getEntityBrightness(float var1)
        {
            float var2 = ((float)particleAge + var1) / (float)particleMaxAge;
            if (var2 < 0.0F)
            {
                var2 = 0.0F;
            }

            if (var2 > 1.0F)
            {
                var2 = 1.0F;
            }

            float var3 = base.getEntityBrightness(var1);
            return var3 * var2 + (1.0F - var2);
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

            moveEntity(motionX, motionY, motionZ);
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