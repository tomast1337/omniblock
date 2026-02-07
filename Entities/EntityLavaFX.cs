using betareborn.Worlds;

namespace betareborn.Entities
{

    public class EntityLavaFX : EntityFX
    {

        private float field_674_a;

        public EntityLavaFX(World var1, double var2, double var4, double var6) : base(var1, var2, var4, var6, 0.0D, 0.0D, 0.0D)
        {
            motionX *= (double)0.8F;
            motionY *= (double)0.8F;
            motionZ *= (double)0.8F;
            motionY = (double)(rand.nextFloat() * 0.4F + 0.05F);
            particleRed = particleGreen = particleBlue = 1.0F;
            particleScale *= rand.nextFloat() * 2.0F + 0.2F;
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
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            if (particleAge++ >= particleMaxAge)
            {
                setEntityDead();
            }

            float var1 = (float)particleAge / (float)particleMaxAge;
            if (rand.nextFloat() > var1)
            {
                worldObj.addParticle("smoke", posX, posY, posZ, motionX, motionY, motionZ);
            }

            motionY -= 0.03D;
            moveEntity(motionX, motionY, motionZ);
            motionX *= (double)0.999F;
            motionY *= (double)0.999F;
            motionZ *= (double)0.999F;
            if (onGround)
            {
                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
            }

        }
    }

}