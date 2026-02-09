using betareborn.Client.Rendering.Core;
using betareborn.Worlds;

namespace betareborn.Entities
{

    public class EntityLavaFX : EntityFX
    {

        private float field_674_a;

        public EntityLavaFX(World var1, double var2, double var4, double var6) : base(var1, var2, var4, var6, 0.0D, 0.0D, 0.0D)
        {
            velocityX *= (double)0.8F;
            velocityY *= (double)0.8F;
            velocityZ *= (double)0.8F;
            velocityY = (double)(random.nextFloat() * 0.4F + 0.05F);
            particleRed = particleGreen = particleBlue = 1.0F;
            particleScale *= random.nextFloat() * 2.0F + 0.2F;
            field_674_a = particleScale;
            particleMaxAge = (int)(16.0D / (java.lang.Math.random() * 0.8D + 0.2D));
            noClip = false;
            particleTextureIndex = 49;
        }

        public override float getEntityBrightness(float var1)
        {
            return 1.0F;
        }

        public override void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            float var8 = ((float)particleAge + var2) / (float)particleMaxAge;
            particleScale = field_674_a * (1.0F - var8 * var8);
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

            float var1 = (float)particleAge / (float)particleMaxAge;
            if (random.nextFloat() > var1)
            {
                world.addParticle("smoke", x, y, z, velocityX, velocityY, velocityZ);
            }

            velocityY -= 0.03D;
            moveEntity(velocityX, velocityY, velocityZ);
            velocityX *= (double)0.999F;
            velocityY *= (double)0.999F;
            velocityZ *= (double)0.999F;
            if (onGround)
            {
                velocityX *= (double)0.7F;
                velocityZ *= (double)0.7F;
            }

        }
    }

}