using betareborn.Client.Rendering.Core;
using betareborn.NBT;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityFX : Entity
    {

        protected int particleTextureIndex;
        protected float particleTextureJitterX;
        protected float particleTextureJitterY;
        protected int particleAge = 0;
        protected int particleMaxAge = 0;
        protected float particleScale;
        protected float particleGravity;
        protected float particleRed;
        protected float particleGreen;
        protected float particleBlue;
        public static double interpPosX;
        public static double interpPosY;
        public static double interpPosZ;

        public EntityFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : base(var1)
        {
            setBoundingBoxSpacing(0.2F, 0.2F);
            yOffset = height / 2.0F;
            setPosition(var2, var4, var6);
            particleRed = particleGreen = particleBlue = 1.0F;
            motionX = var8 + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.4F);
            motionY = var10 + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.4F);
            motionZ = var12 + (double)((float)(java.lang.Math.random() * 2.0D - 1.0D) * 0.4F);
            float var14 = (float)(java.lang.Math.random() + java.lang.Math.random() + 1.0D) * 0.15F;
            float var15 = MathHelper.sqrt_double(motionX * motionX + motionY * motionY + motionZ * motionZ);
            motionX = motionX / (double)var15 * (double)var14 * (double)0.4F;
            motionY = motionY / (double)var15 * (double)var14 * (double)0.4F + (double)0.1F;
            motionZ = motionZ / (double)var15 * (double)var14 * (double)0.4F;
            particleTextureJitterX = rand.nextFloat() * 3.0F;
            particleTextureJitterY = rand.nextFloat() * 3.0F;
            particleScale = (rand.nextFloat() * 0.5F + 0.5F) * 2.0F;
            particleMaxAge = (int)(4.0F / (rand.nextFloat() * 0.9F + 0.1F));
            particleAge = 0;
        }

        public EntityFX func_407_b(float var1)
        {
            motionX *= (double)var1;
            motionY = (motionY - (double)0.1F) * (double)var1 + (double)0.1F;
            motionZ *= (double)var1;
            return this;
        }

        public EntityFX func_405_d(float var1)
        {
            setBoundingBoxSpacing(0.2F * var1, 0.2F * var1);
            particleScale *= var1;
            return this;
        }

        protected override bool canTriggerWalking()
        {
            return false;
        }

        protected override void entityInit()
        {
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

            motionY -= 0.04D * (double)particleGravity;
            moveEntity(motionX, motionY, motionZ);
            motionX *= (double)0.98F;
            motionY *= (double)0.98F;
            motionZ *= (double)0.98F;
            if (onGround)
            {
                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
            }

        }

        public virtual void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            float var8 = (float)(particleTextureIndex % 16) / 16.0F;
            float var9 = var8 + 0.999F / 16.0F;
            float var10 = (float)(particleTextureIndex / 16) / 16.0F;
            float var11 = var10 + 0.999F / 16.0F;
            float var12 = 0.1F * particleScale;
            float var13 = (float)(prevPosX + (posX - prevPosX) * (double)var2 - interpPosX);
            float var14 = (float)(prevPosY + (posY - prevPosY) * (double)var2 - interpPosY);
            float var15 = (float)(prevPosZ + (posZ - prevPosZ) * (double)var2 - interpPosZ);
            float var16 = getEntityBrightness(var2);
            var1.setColorOpaque_F(particleRed * var16, particleGreen * var16, particleBlue * var16);
            var1.addVertexWithUV((double)(var13 - var3 * var12 - var6 * var12), (double)(var14 - var4 * var12), (double)(var15 - var5 * var12 - var7 * var12), (double)var9, (double)var11);
            var1.addVertexWithUV((double)(var13 - var3 * var12 + var6 * var12), (double)(var14 + var4 * var12), (double)(var15 - var5 * var12 + var7 * var12), (double)var9, (double)var10);
            var1.addVertexWithUV((double)(var13 + var3 * var12 + var6 * var12), (double)(var14 + var4 * var12), (double)(var15 + var5 * var12 + var7 * var12), (double)var8, (double)var10);
            var1.addVertexWithUV((double)(var13 + var3 * var12 - var6 * var12), (double)(var14 - var4 * var12), (double)(var15 + var5 * var12 - var7 * var12), (double)var8, (double)var11);
        }

        public virtual int getFXLayer()
        {
            return 0;
        }

        public override void writeNbt(NBTTagCompound var1)
        {
        }

        public override void readNbt(NBTTagCompound var1)
        {
        }
    }

}