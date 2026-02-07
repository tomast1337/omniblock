using betareborn.Items;
using betareborn.Materials;
using betareborn.NBT;
using betareborn.Worlds;
using java.util;

namespace betareborn.Entities
{
    public class EntityPainting : Entity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityPainting).TypeHandle);

        private int field_695_c;
        public int direction;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public EnumArt art;

        public EntityPainting(World var1) : base(var1)
        {
            field_695_c = 0;
            direction = 0;
            yOffset = 0.0F;
            setSize(0.5F, 0.5F);
        }

        public EntityPainting(World var1, int var2, int var3, int var4, int var5) : this(var1)
        {
            xPosition = var2;
            yPosition = var3;
            zPosition = var4;
            ArrayList var6 = new ArrayList();
            EnumArt[] var7 = EnumArt.values;
            int var8 = var7.Length;

            for (int var9 = 0; var9 < var8; ++var9)
            {
                EnumArt var10 = var7[var9];
                art = var10;
                func_412_b(var5);
                if (func_410_i())
                {
                    var6.add(var10);
                }
            }

            if (var6.size() > 0)
            {
                art = (EnumArt)var6.get(rand.nextInt(var6.size()));
            }

            func_412_b(var5);
        }

        public EntityPainting(World var1, int var2, int var3, int var4, int var5, String var6) : this(var1)
        {
            xPosition = var2;
            yPosition = var3;
            zPosition = var4;
            EnumArt[] var7 = EnumArt.values;
            int var8 = var7.Length;

            for (int var9 = 0; var9 < var8; ++var9)
            {
                EnumArt var10 = var7[var9];
                if (var10.title.Equals(var6))
                {
                    art = var10;
                    break;
                }
            }

            func_412_b(var5);
        }

        protected override void entityInit()
        {
        }

        public void func_412_b(int var1)
        {
            direction = var1;
            prevRotationYaw = rotationYaw = (float)(var1 * 90);
            float var2 = (float)art.sizeX;
            float var3 = (float)art.sizeY;
            float var4 = (float)art.sizeX;
            if (var1 != 0 && var1 != 2)
            {
                var2 = 0.5F;
            }
            else
            {
                var4 = 0.5F;
            }

            var2 /= 32.0F;
            var3 /= 32.0F;
            var4 /= 32.0F;
            float var5 = (float)xPosition + 0.5F;
            float var6 = (float)yPosition + 0.5F;
            float var7 = (float)zPosition + 0.5F;
            float var8 = 9.0F / 16.0F;
            if (var1 == 0)
            {
                var7 -= var8;
            }

            if (var1 == 1)
            {
                var5 -= var8;
            }

            if (var1 == 2)
            {
                var7 += var8;
            }

            if (var1 == 3)
            {
                var5 += var8;
            }

            if (var1 == 0)
            {
                var5 -= func_411_c(art.sizeX);
            }

            if (var1 == 1)
            {
                var7 += func_411_c(art.sizeX);
            }

            if (var1 == 2)
            {
                var5 += func_411_c(art.sizeX);
            }

            if (var1 == 3)
            {
                var7 -= func_411_c(art.sizeX);
            }

            var6 += func_411_c(art.sizeY);
            setPosition((double)var5, (double)var6, (double)var7);
            float var9 = -(0.1F / 16.0F);
            boundingBox.set((double)(var5 - var2 - var9), (double)(var6 - var3 - var9), (double)(var7 - var4 - var9), (double)(var5 + var2 + var9), (double)(var6 + var3 + var9), (double)(var7 + var4 + var9));
        }

        private float func_411_c(int var1)
        {
            return var1 == 32 ? 0.5F : (var1 == 64 ? 0.5F : 0.0F);
        }

        public override void onUpdate()
        {
            if (field_695_c++ == 100 && !worldObj.multiplayerWorld)
            {
                field_695_c = 0;
                if (!func_410_i())
                {
                    setEntityDead();
                    worldObj.entityJoinedWorld(new EntityItem(worldObj, posX, posY, posZ, new ItemStack(Item.painting)));
                }
            }

        }

        public bool func_410_i()
        {
            if (worldObj.getCollidingBoundingBoxes(this, boundingBox).Count > 0)
            {
                return false;
            }
            else
            {
                int var1 = art.sizeX / 16;
                int var2 = art.sizeY / 16;
                int var3 = xPosition;
                int var4 = yPosition;
                int var5 = zPosition;
                if (direction == 0)
                {
                    var3 = MathHelper.floor_double(posX - (double)((float)art.sizeX / 32.0F));
                }

                if (direction == 1)
                {
                    var5 = MathHelper.floor_double(posZ - (double)((float)art.sizeX / 32.0F));
                }

                if (direction == 2)
                {
                    var3 = MathHelper.floor_double(posX - (double)((float)art.sizeX / 32.0F));
                }

                if (direction == 3)
                {
                    var5 = MathHelper.floor_double(posZ - (double)((float)art.sizeX / 32.0F));
                }

                var4 = MathHelper.floor_double(posY - (double)((float)art.sizeY / 32.0F));

                int var7;
                for (int var6 = 0; var6 < var1; ++var6)
                {
                    for (var7 = 0; var7 < var2; ++var7)
                    {
                        Material var8;
                        if (direction != 0 && direction != 2)
                        {
                            var8 = worldObj.getBlockMaterial(xPosition, var4 + var7, var5 + var6);
                        }
                        else
                        {
                            var8 = worldObj.getBlockMaterial(var3 + var6, var4 + var7, zPosition);
                        }

                        if (!var8.isSolid())
                        {
                            return false;
                        }
                    }
                }

                var var9 = worldObj.getEntitiesWithinAABBExcludingEntity(this, boundingBox);

                for (var7 = 0; var7 < var9.Count; ++var7)
                {
                    if (var9[var7] is EntityPainting)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public override bool canBeCollidedWith()
        {
            return true;
        }

        public override bool attackEntityFrom(Entity var1, int var2)
        {
            if (!isDead && !worldObj.multiplayerWorld)
            {
                setEntityDead();
                setBeenAttacked();
                worldObj.entityJoinedWorld(new EntityItem(worldObj, posX, posY, posZ, new ItemStack(Item.painting)));
            }

            return true;
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            var1.setByte("Dir", (sbyte)direction);
            var1.setString("Motive", art.title);
            var1.setInteger("TileX", xPosition);
            var1.setInteger("TileY", yPosition);
            var1.setInteger("TileZ", zPosition);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            direction = var1.getByte("Dir");
            xPosition = var1.getInteger("TileX");
            yPosition = var1.getInteger("TileY");
            zPosition = var1.getInteger("TileZ");
            String var2 = var1.getString("Motive");
            EnumArt[] var3 = EnumArt.values;
            int var4 = var3.Length;

            for (int var5 = 0; var5 < var4; ++var5)
            {
                EnumArt var6 = var3[var5];
                if (var6.title.Equals(var2))
                {
                    art = var6;
                }
            }

            if (art == null)
            {
                art = EnumArt.Kebab;
            }

            func_412_b(direction);
        }

        public override void moveEntity(double var1, double var3, double var5)
        {
            if (!worldObj.multiplayerWorld && var1 * var1 + var3 * var3 + var5 * var5 > 0.0D)
            {
                setEntityDead();
                worldObj.entityJoinedWorld(new EntityItem(worldObj, posX, posY, posZ, new ItemStack(Item.painting)));
            }

        }

        public override void addVelocity(double var1, double var3, double var5)
        {
            if (!worldObj.multiplayerWorld && var1 * var1 + var3 * var3 + var5 * var5 > 0.0D)
            {
                setEntityDead();
                worldObj.entityJoinedWorld(new EntityItem(worldObj, posX, posY, posZ, new ItemStack(Item.painting)));
            }

        }
    }

}