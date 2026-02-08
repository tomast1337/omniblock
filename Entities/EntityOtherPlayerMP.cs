using betareborn.Items;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityOtherPlayerMP : EntityPlayer
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityOtherPlayerMP).TypeHandle);

        private int field_785_bg;
        private double field_784_bh;
        private double field_783_bi;
        private double field_782_bj;
        private double field_780_bk;
        private double field_786_bl;
        float field_20924_a = 0.0F;

        public EntityOtherPlayerMP(World var1, String var2) : base(var1)
        {
            username = var2;
            yOffset = 0.0F;
            stepHeight = 0.0F;
            if (var2 != null && var2.Length > 0)
            {
                skinUrl = "http://s3.amazonaws.com/MinecraftSkins/" + var2 + ".png";
            }

            noClip = true;
            field_22062_y = 0.25F;
            renderDistanceWeight = 10.0D;
        }

        protected override void resetHeight()
        {
            yOffset = 0.0F;
        }

        public override bool attackEntityFrom(Entity var1, int var2)
        {
            return true;
        }

        public override void setPositionAndRotation2(double var1, double var3, double var5, float var7, float var8, int var9)
        {
            field_784_bh = var1;
            field_783_bi = var3;
            field_782_bj = var5;
            field_780_bk = (double)var7;
            field_786_bl = (double)var8;
            field_785_bg = var9;
        }

        public override void onUpdate()
        {
            field_22062_y = 0.0F;
            base.onUpdate();
            field_705_Q = field_704_R;
            double var1 = posX - prevPosX;
            double var3 = posZ - prevPosZ;
            float var5 = MathHelper.sqrt_double(var1 * var1 + var3 * var3) * 4.0F;
            if (var5 > 1.0F)
            {
                var5 = 1.0F;
            }

            field_704_R += (var5 - field_704_R) * 0.4F;
            field_703_S += field_704_R;
        }

        public override float getShadowSize()
        {
            return 0.0F;
        }

        public override void onLivingUpdate()
        {
            base.updatePlayerActionState();
            if (field_785_bg > 0)
            {
                double var1 = posX + (field_784_bh - posX) / (double)field_785_bg;
                double var3 = posY + (field_783_bi - posY) / (double)field_785_bg;
                double var5 = posZ + (field_782_bj - posZ) / (double)field_785_bg;

                double var7;
                for (var7 = field_780_bk - (double)rotationYaw; var7 < -180.0D; var7 += 360.0D)
                {
                }

                while (var7 >= 180.0D)
                {
                    var7 -= 360.0D;
                }

                rotationYaw = (float)((double)rotationYaw + var7 / (double)field_785_bg);
                rotationPitch = (float)((double)rotationPitch + (field_786_bl - (double)rotationPitch) / (double)field_785_bg);
                --field_785_bg;
                setPosition(var1, var3, var5);
                setRotation(rotationYaw, rotationPitch);
            }

            field_775_e = field_774_f;
            float var9 = MathHelper.sqrt_double(motionX * motionX + motionZ * motionZ);
            float var2 = (float)java.lang.Math.atan(-motionY * (double)0.2F) * 15.0F;
            if (var9 > 0.1F)
            {
                var9 = 0.1F;
            }

            if (!onGround || health <= 0)
            {
                var9 = 0.0F;
            }

            if (onGround || health <= 0)
            {
                var2 = 0.0F;
            }

            field_774_f += (var9 - field_774_f) * 0.4F;
            field_9328_R += (var2 - field_9328_R) * 0.8F;
        }

        public override void outfitWithItem(int var1, int var2, int var3)
        {
            ItemStack var4 = null;
            if (var2 >= 0)
            {
                var4 = new ItemStack(var2, 1, var3);
            }

            if (var1 == 0)
            {
                inventory.mainInventory[inventory.currentItem] = var4;
            }
            else
            {
                inventory.armorInventory[var1 - 1] = var4;
            }

        }

        public override void func_6420_o()
        {
        }
    }

}