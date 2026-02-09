using betareborn.Blocks;
using betareborn.Blocks.Materials;
using betareborn.Items;
using betareborn.NBT;
using betareborn.Util.Hit;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Entities
{
    public abstract class EntityLiving : Entity
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityLiving).TypeHandle);
        public int heartsHalvesLife = 20;
        public float field_9365_p;
        public float field_9363_r;
        public float renderYawOffset = 0.0F;
        public float prevRenderYawOffset = 0.0F;
        protected float field_9362_u;
        protected float field_9361_v;
        protected float field_9360_w;
        protected float field_9359_x;
        protected bool field_9358_y = true;
        protected string texture = "/mob/char.png";
        protected bool field_9355_A = true;
        protected float field_9353_B = 0.0F;
        protected string field_9351_C = null;
        protected float field_9349_D = 1.0F;
        protected int scoreValue = 0;
        protected float field_9345_F = 0.0F;
        public bool isMultiplayerEntity = false;
        public float prevSwingProgress;
        public float swingProgress;
        public int health = 10;
        public int prevHealth;
        private int livingSoundTime;
        public int hurtTime;
        public int maxHurtTime;
        public float attackedAtYaw = 0.0F;
        public int deathTime = 0;
        public int attackTime = 0;
        public float cameraPitch;
        public float tilt;
        protected bool unused_flag = false;
        public int field_9326_T = -1;
        public float field_9325_U = (float)(java.lang.Math.random() * (double)0.9F + (double)0.1F);
        public float lastWalkAnimationSpeed;
        public float walkAnimationSpeed;
        public float field_703_S;
        protected int newPosRotationIncrements;
        protected double newPosX;
        protected double newPosY;
        protected double newPosZ;
        protected double newRotationYaw;
        protected double newRotationPitch;
        protected int field_9346_af = 0;
        protected int entityAge = 0;
        protected float moveStrafing;
        protected float moveForward;
        protected float randomYawVelocity;
        protected bool isJumping = false;
        protected float defaultPitch = 0.0F;
        protected float moveSpeed = 0.7F;
        private Entity currentTarget;
        protected int numTicksToChaseTarget = 0;

        public EntityLiving(World var1) : base(var1)
        {
            preventEntitySpawning = true;
            field_9363_r = (float)(java.lang.Math.random() + 1.0D) * 0.01F;
            setPosition(x, y, z);
            field_9365_p = (float)java.lang.Math.random() * 12398.0F;
            yaw = (float)(java.lang.Math.random() * (double)((float)java.lang.Math.PI) * 2.0D);
            stepHeight = 0.5F;
        }

        protected override void entityInit()
        {
        }

        public bool canEntityBeSeen(Entity var1)
        {
            return world.raycast(Vec3D.createVector(x, y + (double)getEyeHeight(), z), Vec3D.createVector(var1.x, var1.y + (double)var1.getEyeHeight(), var1.z)) == null;
        }

        public override string getEntityTexture()
        {
            return texture;
        }

        public override bool canBeCollidedWith()
        {
            return !isDead;
        }

        public override bool canBePushed()
        {
            return !isDead;
        }

        public override float getEyeHeight()
        {
            return height * 0.85F;
        }

        public virtual int getTalkInterval()
        {
            return 80;
        }

        public void playLivingSound()
        {
            string var1 = getLivingSound();
            if (var1 != null)
            {
                world.playSound(this, var1, getSoundVolume(), (random.nextFloat() - random.nextFloat()) * 0.2F + 1.0F);
            }

        }

        public override void onEntityUpdate()
        {
            prevSwingProgress = swingProgress;
            base.onEntityUpdate();
            if (random.nextInt(1000) < livingSoundTime++)
            {
                livingSoundTime = -getTalkInterval();
                playLivingSound();
            }

            if (isEntityAlive() && isInsideWall())
            {
                damage(null, 1);
            }

            if (isImmuneToFire || world.isRemote)
            {
                fireTicks = 0;
            }

            int var1;
            if (isEntityAlive() && isInsideOfMaterial(Material.WATER) && !canBreatheUnderwater())
            {
                --air;
                if (air == -20)
                {
                    air = 0;

                    for (var1 = 0; var1 < 8; ++var1)
                    {
                        float var2 = random.nextFloat() - random.nextFloat();
                        float var3 = random.nextFloat() - random.nextFloat();
                        float var4 = random.nextFloat() - random.nextFloat();
                        world.addParticle("bubble", x + (double)var2, y + (double)var3, z + (double)var4, velocityX, velocityY, velocityZ);
                    }

                    damage(null, 2);
                }

                fireTicks = 0;
            }
            else
            {
                air = maxAir;
            }

            cameraPitch = tilt;
            if (attackTime > 0)
            {
                --attackTime;
            }

            if (hurtTime > 0)
            {
                --hurtTime;
            }

            if (hearts > 0)
            {
                --hearts;
            }

            if (health <= 0)
            {
                ++deathTime;
                if (deathTime > 20)
                {
                    onEntityDeath();
                    markDead();

                    for (var1 = 0; var1 < 20; ++var1)
                    {
                        double var8 = random.nextGaussian() * 0.02D;
                        double var9 = random.nextGaussian() * 0.02D;
                        double var6 = random.nextGaussian() * 0.02D;
                        world.addParticle("explode", x + (double)(random.nextFloat() * width * 2.0F) - (double)width, y + (double)(random.nextFloat() * height), z + (double)(random.nextFloat() * width * 2.0F) - (double)width, var8, var9, var6);
                    }
                }
            }

            field_9359_x = field_9360_w;
            prevRenderYawOffset = renderYawOffset;
            prevYaw = yaw;
            prevPitch = pitch;
        }

        //TODO: will this still work properly when we implement the server?
        public override void moveEntity(double var1, double var3, double var5)
        {
            if (!isMultiplayerEntity || this is ClientPlayerEntity) base.moveEntity(var1, var3, var5);
        }

        public void animateSpawn()
        {
            for (int var1 = 0; var1 < 20; ++var1)
            {
                double var2 = random.nextGaussian() * 0.02D;
                double var4 = random.nextGaussian() * 0.02D;
                double var6 = random.nextGaussian() * 0.02D;
                double var8 = 10.0D;
                world.addParticle("explode", x + (double)(random.nextFloat() * width * 2.0F) - (double)width - var2 * var8, y + (double)(random.nextFloat() * height) - var4 * var8, z + (double)(random.nextFloat() * width * 2.0F) - (double)width - var6 * var8, var2, var4, var6);
            }

        }

        public override void updateRidden()
        {
            base.updateRidden();
            field_9362_u = field_9361_v;
            field_9361_v = 0.0F;
        }

        public override void setPositionAndRotation2(double var1, double var3, double var5, float var7, float var8, int var9)
        {
            standingEyeHeight = 0.0F;
            newPosX = var1;
            newPosY = var3;
            newPosZ = var5;
            newRotationYaw = (double)var7;
            newRotationPitch = (double)var8;
            newPosRotationIncrements = var9;
        }

        public override void onUpdate()
        {
            base.onUpdate();
            tickMovement();
            double var1 = x - prevX;
            double var3 = z - prevZ;
            float var5 = MathHelper.sqrt_double(var1 * var1 + var3 * var3);
            float var6 = renderYawOffset;
            float var7 = 0.0F;
            field_9362_u = field_9361_v;
            float var8 = 0.0F;
            if (var5 > 0.05F)
            {
                var8 = 1.0F;
                var7 = var5 * 3.0F;
                var6 = (float)java.lang.Math.atan2(var3, var1) * 180.0F / (float)java.lang.Math.PI - 90.0F;
            }

            if (swingProgress > 0.0F)
            {
                var6 = yaw;
            }

            if (!onGround)
            {
                var8 = 0.0F;
            }

            field_9361_v += (var8 - field_9361_v) * 0.3F;

            float var9;
            for (var9 = var6 - renderYawOffset; var9 < -180.0F; var9 += 360.0F)
            {
            }

            while (var9 >= 180.0F)
            {
                var9 -= 360.0F;
            }

            renderYawOffset += var9 * 0.3F;

            float var10;
            for (var10 = yaw - renderYawOffset; var10 < -180.0F; var10 += 360.0F)
            {
            }

            while (var10 >= 180.0F)
            {
                var10 -= 360.0F;
            }

            bool var11 = var10 < -90.0F || var10 >= 90.0F;
            if (var10 < -75.0F)
            {
                var10 = -75.0F;
            }

            if (var10 >= 75.0F)
            {
                var10 = 75.0F;
            }

            renderYawOffset = yaw - var10;
            if (var10 * var10 > 2500.0F)
            {
                renderYawOffset += var10 * 0.2F;
            }

            if (var11)
            {
                var7 *= -1.0F;
            }

            while (yaw - prevYaw < -180.0F)
            {
                prevYaw -= 360.0F;
            }

            while (yaw - prevYaw >= 180.0F)
            {
                prevYaw += 360.0F;
            }

            while (renderYawOffset - prevRenderYawOffset < -180.0F)
            {
                prevRenderYawOffset -= 360.0F;
            }

            while (renderYawOffset - prevRenderYawOffset >= 180.0F)
            {
                prevRenderYawOffset += 360.0F;
            }

            while (pitch - prevPitch < -180.0F)
            {
                prevPitch -= 360.0F;
            }

            while (pitch - prevPitch >= 180.0F)
            {
                prevPitch += 360.0F;
            }

            field_9360_w += var7;
        }

        protected override void setBoundingBoxSpacing(float var1, float var2)
        {
            base.setBoundingBoxSpacing(var1, var2);
        }

        public virtual void heal(int var1)
        {
            if (health > 0)
            {
                health += var1;
                if (health > 20)
                {
                    health = 20;
                }

                hearts = heartsHalvesLife / 2;
            }
        }

        public override bool damage(Entity var1, int var2)
        {
            if (world.isRemote)
            {
                return false;
            }
            else
            {
                entityAge = 0;
                if (health <= 0)
                {
                    return false;
                }
                else
                {
                    walkAnimationSpeed = 1.5F;
                    bool var3 = true;
                    if ((float)hearts > (float)heartsHalvesLife / 2.0F)
                    {
                        if (var2 <= field_9346_af)
                        {
                            return false;
                        }

                        applyDamage(var2 - field_9346_af);
                        field_9346_af = var2;
                        var3 = false;
                    }
                    else
                    {
                        field_9346_af = var2;
                        prevHealth = health;
                        hearts = heartsHalvesLife;
                        applyDamage(var2);
                        hurtTime = maxHurtTime = 10;
                    }

                    attackedAtYaw = 0.0F;
                    if (var3)
                    {
                        world.broadcastEntityEvent(this, (byte)2);
                        setBeenAttacked();
                        if (var1 != null)
                        {
                            double var4 = var1.x - x;

                            double var6;
                            for (var6 = var1.z - z; var4 * var4 + var6 * var6 < 1.0E-4D; var6 = (java.lang.Math.random() - java.lang.Math.random()) * 0.01D)
                            {
                                var4 = (java.lang.Math.random() - java.lang.Math.random()) * 0.01D;
                            }

                            attackedAtYaw = (float)(java.lang.Math.atan2(var6, var4) * 180.0D / (double)((float)java.lang.Math.PI)) - yaw;
                            knockBack(var1, var2, var4, var6);
                        }
                        else
                        {
                            attackedAtYaw = (float)((int)(java.lang.Math.random() * 2.0D) * 180);
                        }
                    }

                    if (health <= 0)
                    {
                        if (var3)
                        {
                            world.playSound(this, getDeathSound(), getSoundVolume(), (random.nextFloat() - random.nextFloat()) * 0.2F + 1.0F);
                        }

                        onKilledBy(var1);
                    }
                    else if (var3)
                    {
                        world.playSound(this, getHurtSound(), getSoundVolume(), (random.nextFloat() - random.nextFloat()) * 0.2F + 1.0F);
                    }

                    return true;
                }
            }
        }

        public override void performHurtAnimation()
        {
            hurtTime = maxHurtTime = 10;
            attackedAtYaw = 0.0F;
        }

        protected virtual void applyDamage(int var1)
        {
            health -= var1;
        }

        protected virtual float getSoundVolume()
        {
            return 1.0F;
        }

        protected virtual string getLivingSound()
        {
            return null;
        }

        protected virtual string getHurtSound()
        {
            return "random.hurt";
        }

        protected virtual string getDeathSound()
        {
            return "random.hurt";
        }

        public void knockBack(Entity var1, int var2, double var3, double var5)
        {
            float var7 = MathHelper.sqrt_double(var3 * var3 + var5 * var5);
            float var8 = 0.4F;
            velocityX /= 2.0D;
            velocityY /= 2.0D;
            velocityZ /= 2.0D;
            velocityX -= var3 / (double)var7 * (double)var8;
            velocityY += (double)0.4F;
            velocityZ -= var5 / (double)var7 * (double)var8;
            if (velocityY > (double)0.4F)
            {
                velocityY = (double)0.4F;
            }

        }

        public virtual void onKilledBy(Entity var1)
        {
            if (scoreValue >= 0 && var1 != null)
            {
                var1.updateKilledAchievement(this, scoreValue);
            }

            if (var1 != null)
            {
                var1.onKillOther(this);
            }

            unused_flag = true;
            if (!world.isRemote)
            {
                dropFewItems();
            }

            world.broadcastEntityEvent(this, (byte)3);
        }

        protected virtual void dropFewItems()
        {
            int var1 = getDropItemId();
            if (var1 > 0)
            {
                int var2 = random.nextInt(3);

                for (int var3 = 0; var3 < var2; ++var3)
                {
                    dropItem(var1, 1);
                }
            }

        }

        protected virtual int getDropItemId()
        {
            return 0;
        }

        protected override void onLanding(float var1)
        {
            base.onLanding(var1);
            int var2 = (int)java.lang.Math.ceil((double)(var1 - 3.0F));
            if (var2 > 0)
            {
                damage(null, var2);
                int var3 = world.getBlockId(MathHelper.floor_double(x), MathHelper.floor_double(y - (double)0.2F - (double)standingEyeHeight), MathHelper.floor_double(z));
                if (var3 > 0)
                {
                    BlockSoundGroup var4 = Block.BLOCKS[var3].soundGroup;
                    world.playSound(this, var4.func_1145_d(), var4.getVolume() * 0.5F, var4.getPitch() * (12.0F / 16.0F));
                }
            }

        }

        public virtual void travel(float var1, float var2)
        {
            double var3;
            if (isInWater())
            {
                var3 = y;
                moveFlying(var1, var2, 0.02F);
                moveEntity(velocityX, velocityY, velocityZ);
                velocityX *= (double)0.8F;
                velocityY *= (double)0.8F;
                velocityZ *= (double)0.8F;
                velocityY -= 0.02D;
                if (horizontalCollison && isOffsetPositionInLiquid(velocityX, velocityY + (double)0.6F - y + var3, velocityZ))
                {
                    velocityY = (double)0.3F;
                }
            }
            else if (handleLavaMovement())
            {
                var3 = y;
                moveFlying(var1, var2, 0.02F);
                moveEntity(velocityX, velocityY, velocityZ);
                velocityX *= 0.5D;
                velocityY *= 0.5D;
                velocityZ *= 0.5D;
                velocityY -= 0.02D;
                if (horizontalCollison && isOffsetPositionInLiquid(velocityX, velocityY + (double)0.6F - y + var3, velocityZ))
                {
                    velocityY = (double)0.3F;
                }
            }
            else
            {
                float var8 = 0.91F;
                if (onGround)
                {
                    var8 = 546.0F * 0.1F * 0.1F * 0.1F;
                    int var4 = world.getBlockId(MathHelper.floor_double(x), MathHelper.floor_double(boundingBox.minY) - 1, MathHelper.floor_double(z));
                    if (var4 > 0)
                    {
                        var8 = Block.BLOCKS[var4].slipperiness * 0.91F;
                    }
                }

                float var9 = 0.16277136F / (var8 * var8 * var8);
                moveFlying(var1, var2, onGround ? 0.1F * var9 : 0.02F);
                var8 = 0.91F;
                if (onGround)
                {
                    var8 = 546.0F * 0.1F * 0.1F * 0.1F;
                    int var5 = world.getBlockId(MathHelper.floor_double(x), MathHelper.floor_double(boundingBox.minY) - 1, MathHelper.floor_double(z));
                    if (var5 > 0)
                    {
                        var8 = Block.BLOCKS[var5].slipperiness * 0.91F;
                    }
                }

                if (isOnLadder())
                {
                    float var10 = 0.15F;
                    if (velocityX < (double)(-var10))
                    {
                        velocityX = (double)(-var10);
                    }

                    if (velocityX > (double)var10)
                    {
                        velocityX = (double)var10;
                    }

                    if (velocityZ < (double)(-var10))
                    {
                        velocityZ = (double)(-var10);
                    }

                    if (velocityZ > (double)var10)
                    {
                        velocityZ = (double)var10;
                    }

                    fallDistance = 0.0F;
                    if (velocityY < -0.15D)
                    {
                        velocityY = -0.15D;
                    }

                    if (isSneaking() && velocityY < 0.0D)
                    {
                        velocityY = 0.0D;
                    }
                }

                moveEntity(velocityX, velocityY, velocityZ);
                if (horizontalCollison && isOnLadder())
                {
                    velocityY = 0.2D;
                }

                velocityY -= 0.08D;
                velocityY *= (double)0.98F;
                velocityX *= (double)var8;
                velocityZ *= (double)var8;
            }

            lastWalkAnimationSpeed = walkAnimationSpeed;
            var3 = x - prevX;
            double var11 = z - prevZ;
            float var7 = MathHelper.sqrt_double(var3 * var3 + var11 * var11) * 4.0F;
            if (var7 > 1.0F)
            {
                var7 = 1.0F;
            }

            walkAnimationSpeed += (var7 - walkAnimationSpeed) * 0.4F;
            field_703_S += walkAnimationSpeed;
        }

        public virtual bool isOnLadder()
        {
            int var1 = MathHelper.floor_double(x);
            int var2 = MathHelper.floor_double(boundingBox.minY);
            int var3 = MathHelper.floor_double(z);
            return world.getBlockId(var1, var2, var3) == Block.LADDER.id;
        }

        public override void writeNbt(NBTTagCompound var1)
        {
            var1.setShort("Health", (short)health);
            var1.setShort("HurtTime", (short)hurtTime);
            var1.setShort("DeathTime", (short)deathTime);
            var1.setShort("AttackTime", (short)attackTime);
        }

        public override void readNbt(NBTTagCompound var1)
        {
            health = var1.getShort("Health");
            if (!var1.hasKey("Health"))
            {
                health = 10;
            }

            hurtTime = var1.getShort("HurtTime");
            deathTime = var1.getShort("DeathTime");
            attackTime = var1.getShort("AttackTime");
        }

        public override bool isEntityAlive()
        {
            return !isDead && health > 0;
        }

        public virtual bool canBreatheUnderwater()
        {
            return false;
        }

        public virtual void tickMovement()
        {
            if (newPosRotationIncrements > 0)
            {
                double var1 = x + (newPosX - x) / (double)newPosRotationIncrements;
                double var3 = y + (newPosY - y) / (double)newPosRotationIncrements;
                double var5 = z + (newPosZ - z) / (double)newPosRotationIncrements;

                double var7;
                for (var7 = newRotationYaw - (double)yaw; var7 < -180.0D; var7 += 360.0D)
                {
                }

                while (var7 >= 180.0D)
                {
                    var7 -= 360.0D;
                }

                yaw = (float)((double)yaw + var7 / (double)newPosRotationIncrements);
                pitch = (float)((double)pitch + (newRotationPitch - (double)pitch) / (double)newPosRotationIncrements);
                --newPosRotationIncrements;
                setPosition(var1, var3, var5);
                setRotation(yaw, pitch);
                var var9 = world.getEntityCollisions(this, boundingBox.contract(1.0D / 32.0D, 0.0D, 1.0D / 32.0D));
                if (var9.Count > 0)
                {
                    double var10 = 0.0D;

                    for (int var12 = 0; var12 < var9.Count; ++var12)
                    {
                        Box var13 = var9[var12];
                        if (var13.maxY > var10)
                        {
                            var10 = var13.maxY;
                        }
                    }

                    var3 += var10 - boundingBox.minY;
                    setPosition(var1, var3, var5);
                }
            }

            if (isMovementBlocked())
            {
                isJumping = false;
                moveStrafing = 0.0F;
                moveForward = 0.0F;
                randomYawVelocity = 0.0F;
            }
            else if (!isMultiplayerEntity)
            {
                tickLiving();
            }

            bool var14 = isInWater();
            bool var2 = handleLavaMovement();
            if (isJumping)
            {
                if (var14)
                {
                    velocityY += (double)0.04F;
                }
                else if (var2)
                {
                    velocityY += (double)0.04F;
                }
                else if (onGround)
                {
                    jump();
                }
            }

            moveStrafing *= 0.98F;
            moveForward *= 0.98F;
            randomYawVelocity *= 0.9F;
            travel(moveStrafing, moveForward);
            var var15 = world.getEntities(this, boundingBox.expand((double)0.2F, 0.0D, (double)0.2F));
            if (var15 != null && var15.Count > 0)
            {
                for (int var4 = 0; var4 < var15.Count; ++var4)
                {
                    Entity var16 = var15[var4];
                    if (var16.canBePushed())
                    {
                        var16.applyEntityCollision(this);
                    }
                }
            }

        }

        protected virtual bool isMovementBlocked()
        {
            return health <= 0;
        }

        protected virtual void jump()
        {
            velocityY = (double)0.42F;
        }

        protected virtual bool canDespawn()
        {
            return true;
        }

        protected void func_27021_X()
        {
            EntityPlayer var1 = world.getClosestPlayer(this, -1.0D);
            if (canDespawn() && var1 != null)
            {
                double var2 = var1.x - x;
                double var4 = var1.y - y;
                double var6 = var1.z - z;
                double var8 = var2 * var2 + var4 * var4 + var6 * var6;
                if (var8 > 16384.0D)
                {
                    markDead();
                }

                if (entityAge > 600 && random.nextInt(800) == 0)
                {
                    if (var8 < 1024.0D)
                    {
                        entityAge = 0;
                    }
                    else
                    {
                        markDead();
                    }
                }
            }

        }

        public virtual void tickLiving()
        {
            ++entityAge;
            EntityPlayer var1 = world.getClosestPlayer(this, -1.0D);
            func_27021_X();
            moveStrafing = 0.0F;
            moveForward = 0.0F;
            float var2 = 8.0F;
            if (random.nextFloat() < 0.02F)
            {
                var1 = world.getClosestPlayer(this, (double)var2);
                if (var1 != null)
                {
                    currentTarget = var1;
                    numTicksToChaseTarget = 10 + random.nextInt(20);
                }
                else
                {
                    randomYawVelocity = (random.nextFloat() - 0.5F) * 20.0F;
                }
            }

            if (currentTarget != null)
            {
                faceEntity(currentTarget, 10.0F, (float)func_25026_x());
                if (numTicksToChaseTarget-- <= 0 || currentTarget.isDead || currentTarget.getDistanceSqToEntity(this) > (double)(var2 * var2))
                {
                    currentTarget = null;
                }
            }
            else
            {
                if (random.nextFloat() < 0.05F)
                {
                    randomYawVelocity = (random.nextFloat() - 0.5F) * 20.0F;
                }

                yaw += randomYawVelocity;
                pitch = defaultPitch;
            }

            bool var3 = isInWater();
            bool var4 = handleLavaMovement();
            if (var3 || var4)
            {
                isJumping = random.nextFloat() < 0.8F;
            }

        }

        protected virtual int func_25026_x()
        {
            return 40;
        }

        public void faceEntity(Entity var1, float var2, float var3)
        {
            double var4 = var1.x - x;
            double var8 = var1.z - z;
            double var6;
            if (var1 is EntityLiving)
            {
                EntityLiving var10 = (EntityLiving)var1;
                var6 = y + (double)getEyeHeight() - (var10.y + (double)var10.getEyeHeight());
            }
            else
            {
                var6 = (var1.boundingBox.minY + var1.boundingBox.maxY) / 2.0D - (y + (double)getEyeHeight());
            }

            double var14 = (double)MathHelper.sqrt_double(var4 * var4 + var8 * var8);
            float var12 = (float)(java.lang.Math.atan2(var8, var4) * 180.0D / (double)((float)java.lang.Math.PI)) - 90.0F;
            float var13 = (float)(-(java.lang.Math.atan2(var6, var14) * 180.0D / (double)((float)java.lang.Math.PI)));
            pitch = -updateRotation(pitch, var13, var3);
            yaw = updateRotation(yaw, var12, var2);
        }

        public bool hasCurrentTarget()
        {
            return currentTarget != null;
        }

        public Entity getCurrentTarget()
        {
            return currentTarget;
        }

        private float updateRotation(float var1, float var2, float var3)
        {
            float var4;
            for (var4 = var2 - var1; var4 < -180.0F; var4 += 360.0F)
            {
            }

            while (var4 >= 180.0F)
            {
                var4 -= 360.0F;
            }

            if (var4 > var3)
            {
                var4 = var3;
            }

            if (var4 < -var3)
            {
                var4 = -var3;
            }

            return var1 + var4;
        }

        public void onEntityDeath()
        {
        }

        public virtual bool canSpawn()
        {
            return world.canSpawnEntity(boundingBox) && world.getEntityCollisions(this, boundingBox).Count == 0 && !world.isBoxSubmergedInFluid(boundingBox);
        }

        protected override void kill()
        {
            damage(null, 4);
        }

        public float getSwingProgress(float var1)
        {
            float var2 = swingProgress - prevSwingProgress;
            if (var2 < 0.0F)
            {
                ++var2;
            }

            return prevSwingProgress + var2 * var1;
        }

        public Vec3D getPosition(float var1)
        {
            if (var1 == 1.0F)
            {
                return Vec3D.createVector(x, y, z);
            }
            else
            {
                double var2 = prevX + (x - prevX) * (double)var1;
                double var4 = prevY + (y - prevY) * (double)var1;
                double var6 = prevZ + (z - prevZ) * (double)var1;
                return Vec3D.createVector(var2, var4, var6);
            }
        }

        public override Vec3D getLookVec()
        {
            return getLook(1.0F);
        }

        public Vec3D getLook(float var1)
        {
            float var2;
            float var3;
            float var4;
            float var5;
            if (var1 == 1.0F)
            {
                var2 = MathHelper.cos(-yaw * ((float)java.lang.Math.PI / 180.0F) - (float)java.lang.Math.PI);
                var3 = MathHelper.sin(-yaw * ((float)java.lang.Math.PI / 180.0F) - (float)java.lang.Math.PI);
                var4 = -MathHelper.cos(-pitch * ((float)java.lang.Math.PI / 180.0F));
                var5 = MathHelper.sin(-pitch * ((float)java.lang.Math.PI / 180.0F));
                return Vec3D.createVector((double)(var3 * var4), (double)var5, (double)(var2 * var4));
            }
            else
            {
                var2 = prevPitch + (pitch - prevPitch) * var1;
                var3 = prevYaw + (yaw - prevYaw) * var1;
                var4 = MathHelper.cos(-var3 * ((float)java.lang.Math.PI / 180.0F) - (float)java.lang.Math.PI);
                var5 = MathHelper.sin(-var3 * ((float)java.lang.Math.PI / 180.0F) - (float)java.lang.Math.PI);
                float var6 = -MathHelper.cos(-var2 * ((float)java.lang.Math.PI / 180.0F));
                float var7 = MathHelper.sin(-var2 * ((float)java.lang.Math.PI / 180.0F));
                return Vec3D.createVector((double)(var5 * var6), (double)var7, (double)(var4 * var6));
            }
        }

        public HitResult rayTrace(double var1, float var3)
        {
            Vec3D var4 = getPosition(var3);
            Vec3D var5 = getLook(var3);
            Vec3D var6 = var4.addVector(var5.xCoord * var1, var5.yCoord * var1, var5.zCoord * var1);
            return world.raycast(var4, var6);
        }

        public virtual int getMaxSpawnedInChunk()
        {
            return 4;
        }

        public virtual ItemStack getHeldItem()
        {
            return null;
        }

        public override void handleHealthUpdate(sbyte var1)
        {
            if (var1 == 2)
            {
                walkAnimationSpeed = 1.5F;
                hearts = heartsHalvesLife;
                hurtTime = maxHurtTime = 10;
                attackedAtYaw = 0.0F;
                world.playSound(this, getHurtSound(), getSoundVolume(), (random.nextFloat() - random.nextFloat()) * 0.2F + 1.0F);
                damage(null, 0);
            }
            else if (var1 == 3)
            {
                world.playSound(this, getDeathSound(), getSoundVolume(), (random.nextFloat() - random.nextFloat()) * 0.2F + 1.0F);
                health = 0;
                onKilledBy(null);
            }
            else
            {
                base.handleHealthUpdate(var1);
            }

        }

        public virtual bool isSleeping()
        {
            return false;
        }

        public virtual int getItemStackTextureId(ItemStack var1)
        {
            return var1.getTextureId();
        }
    }
}