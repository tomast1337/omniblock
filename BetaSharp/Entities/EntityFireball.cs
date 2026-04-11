using BetaSharp.NBT;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityFireball : Entity
{
    public override EntityType Type => EntityRegistry.Fireball;
    private int blockX = -1;
    private int blockY = -1;
    private int blockZ = -1;
    private int blockId;
    private bool inGround;
    public int shake;
    public EntityLiving owner;
    private int removalTimer;
    private int inAirTime;
    public double powerX;
    public double powerY;
    public double powerZ;

    public EntityFireball(IWorldContext world) : base(world)
    {
        setBoundingBoxSpacing(1.0F, 1.0F);
    }


    public override bool shouldRender(double var1)
    {
        double var3 = BoundingBox.AverageEdgeLength * 4.0D;
        var3 *= 64.0D;
        return var1 < var3 * var3;
    }

    public EntityFireball(IWorldContext world, double x, double y, double z, double var8, double var10, double var12) : base(world)
    {
        setBoundingBoxSpacing(1.0F, 1.0F);
        setPositionAndAnglesKeepPrevAngles(x, y, z, Yaw, Pitch);
        setPosition(x, y, z);
        double var14 = (double)MathHelper.Sqrt(var8 * var8 + var10 * var10 + var12 * var12);
        powerX = var8 / var14 * 0.1D;
        powerY = var10 / var14 * 0.1D;
        powerZ = var12 / var14 * 0.1D;
    }

    public EntityFireball(IWorldContext world, EntityLiving var2, double var3, double var5, double var7) : base(world)
    {
        owner = var2;
        setBoundingBoxSpacing(1.0F, 1.0F);
        setPositionAndAnglesKeepPrevAngles(var2.X, var2.Y, var2.Z, var2.Yaw, var2.Pitch);
        setPosition(X, Y, Z);
        StandingEyeHeight = 0.0F;
        VelocityX = VelocityY = VelocityZ = 0.0D;
        var3 += Random.NextGaussian() * 0.4D;
        var5 += Random.NextGaussian() * 0.4D;
        var7 += Random.NextGaussian() * 0.4D;
        double var9 = (double)MathHelper.Sqrt(var3 * var3 + var5 * var5 + var7 * var7);
        powerX = var3 / var9 * 0.1D;
        powerY = var5 / var9 * 0.1D;
        powerZ = var7 / var9 * 0.1D;
    }

    public override void tick()
    {
        base.tick();
        FireTicks = 10;
        if (shake > 0)
        {
            --shake;
        }

        if (inGround)
        {
            int var1 = World.Reader.GetBlockId(blockX, blockY, blockZ);
            if (var1 == blockId)
            {
                ++removalTimer;
                if (removalTimer == 1200)
                {
                    markDead();
                }

                return;
            }

            inGround = false;
            VelocityX *= (double)(Random.NextFloat() * 0.2F);
            VelocityY *= (double)(Random.NextFloat() * 0.2F);
            VelocityZ *= (double)(Random.NextFloat() * 0.2F);
            removalTimer = 0;
            inAirTime = 0;
        }
        else
        {
            ++inAirTime;
        }

        Vec3D var15 = new Vec3D(X, Y, Z);
        Vec3D var2 = new Vec3D(X + VelocityX, Y + VelocityY, Z + VelocityZ);
        HitResult var3 = World.Reader.Raycast(var15, var2);
        var15 = new Vec3D(X, Y, Z);
        var2 = new Vec3D(X + VelocityX, Y + VelocityY, Z + VelocityZ);
        if (var3.Type != HitResultType.MISS)
        {
            var2 = new Vec3D(var3.Pos.x, var3.Pos.y, var3.Pos.z);
        }

        Entity var4 = null;
        var var5 = World.Entities.GetEntities(this, BoundingBox.Stretch(VelocityX, VelocityY, VelocityZ).Expand(1.0D, 1.0D, 1.0D));
        double var6 = 0.0D;

        for (int var8 = 0; var8 < var5.Count; ++var8)
        {
            Entity var9 = var5[var8];
            if (var9.isCollidable() && (var9 != owner || inAirTime >= 25))
            {
                float var10 = 0.3F;
                Box var11 = var9.BoundingBox.Expand((double)var10, (double)var10, (double)var10);
                HitResult var12 = var11.Raycast(var15, var2);
                if (var12.Type != HitResultType.MISS)
                {
                    double var13 = var15.distanceTo(var12.Pos);
                    if (var13 < var6 || var6 == 0.0D)
                    {
                        var4 = var9;
                        var6 = var13;
                    }
                }
            }
        }

        if (var4 != null)
        {
            var3 = new HitResult(var4);
        }

        if (var3.Type != HitResultType.MISS)
        {
            if (!World.IsRemote)
            {
                if (var3.Entity != null && var3.Entity.damage(owner, 0))
                {
                }

                World.CreateExplosion(null, X, Y, Z, 1.0F, true);
            }

            markDead();
        }

        X += VelocityX;
        Y += VelocityY;
        Z += VelocityZ;
        float var16 = MathHelper.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
        Yaw = (float)(System.Math.Atan2(VelocityX, VelocityZ) * 180.0D / (double)((float)Math.PI));

        for (Pitch = (float)(System.Math.Atan2(VelocityY, (double)var16) * 180.0D / (double)((float)Math.PI)); Pitch - PrevPitch < -180.0F; PrevPitch -= 360.0F)
        {
        }

        while (Pitch - PrevPitch >= 180.0F)
        {
            PrevPitch += 360.0F;
        }

        while (Yaw - PrevYaw < -180.0F)
        {
            PrevYaw -= 360.0F;
        }

        while (Yaw - PrevYaw >= 180.0F)
        {
            PrevYaw += 360.0F;
        }

        Pitch = PrevPitch + (Pitch - PrevPitch) * 0.2F;
        Yaw = PrevYaw + (Yaw - PrevYaw) * 0.2F;
        float var17 = 0.95F;
        if (isInWater())
        {
            for (int var18 = 0; var18 < 4; ++var18)
            {
                float var19 = 0.25F;
                World.Broadcaster.AddParticle("bubble", X - VelocityX * (double)var19, Y - VelocityY * (double)var19, Z - VelocityZ * (double)var19, VelocityX, VelocityY, VelocityZ);
            }

            var17 = 0.8F;
        }

        VelocityX += powerX;
        VelocityY += powerY;
        VelocityZ += powerZ;
        VelocityX *= (double)var17;
        VelocityY *= (double)var17;
        VelocityZ *= (double)var17;
        World.Broadcaster.AddParticle("smoke", X, Y + 0.5D, Z, 0.0D, 0.0D, 0.0D);
        setPosition(X, Y, Z);
    }

    public override void writeNbt(NBTTagCompound nbt)
    {
        nbt.SetShort("xTile", (short)blockX);
        nbt.SetShort("yTile", (short)blockY);
        nbt.SetShort("zTile", (short)blockZ);
        nbt.SetByte("inTile", (sbyte)blockId);
        nbt.SetByte("shake", (sbyte)shake);
        nbt.SetByte("inGround", (sbyte)(inGround ? 1 : 0));
    }

    public override void readNbt(NBTTagCompound nbt)
    {
        blockX = nbt.GetShort("xTile");
        blockY = nbt.GetShort("yTile");
        blockZ = nbt.GetShort("zTile");
        blockId = nbt.GetByte("inTile") & 255;
        shake = nbt.GetByte("shake") & 255;
        inGround = nbt.GetByte("inGround") == 1;
    }

    public override bool isCollidable()
    {
        return true;
    }

    public override float getTargetingMargin()
    {
        return 1.0F;
    }

    public override bool damage(Entity entity, int amount)
    {
        scheduleVelocityUpdate();
        if (entity != null)
        {
            Vec3D? var3 = entity.getLookVector();
            if (var3 != null)
            {
                VelocityX = var3.Value.x;
                VelocityY = var3.Value.y;
                VelocityZ = var3.Value.z;
                powerX = VelocityX * 0.1D;
                powerY = VelocityY * 0.1D;
                powerZ = VelocityZ * 0.1D;
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    public override float getShadowRadius()
    {
        return 0.0F;
    }
}
