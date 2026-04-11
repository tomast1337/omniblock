using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Math = System.Math;

namespace BetaSharp.Entities;

public abstract class Entity
{
    public abstract EntityType? Type { get; }
    private static int _nextEntityID;
    public int ID { get; set; } = _nextEntityID++;
    public double RenderDistanceWeight { get; set; } = 1.0D;
    public bool PreventEntitySpawning { get; set; } = false;
    public Entity? Passenger { get; set; }
    public Entity? Vehicle { get; set; }
    public IWorldContext World { get; set; }
    public double PrevX { get; set; }
    public double PrevY { get; set; }
    public double PrevZ { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double VelocityZ { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float PrevYaw { get; set; }
    public float PrevPitch { get; set; }
    public Box BoundingBox { get; set; } = new Box(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D);
    public bool OnGround { get; set; }
    public bool HorizontalCollison { get; set; }
    public bool VerticalCollision { get; set; }
    public bool HasCollided { get; set; }
    public bool VelocityModified { get; set; }
    public bool Slowed { get; set; }
    public bool KeepVelocityOnCollision { get; set; } = true;
    public bool Dead { get; set; }
    public float StandingEyeHeight { get; set; } = 0.0F;
    public float Width { get; set; } = 0.6F;
    public float Height { get; set; } = 1.8F;
    public float PrevHorizontalSpeed { get; set; }
    public float HorizontalSpeed { get; set; }
    protected float FallDistance { get; set; }
    private int _nextStepSoundDistance = 1;
    public double LastTickX { get; set; }
    public double LastTickY { get; set; }
    public double LastTickZ { get; set; }
    public float CameraOffset { get; set; }
    public float StepHeight { get; set; } = 0.0F;
    public bool NoClip { get; set; } = false;
    public float PushSpeedReduction { get; set; } = 0.0F;
    protected JavaRandom Random { get; set; } = new();
    public int Age { get; set; } = 0;
    public int FireImmunityTicks { get; set; } = 1;
    public int FireTicks { get; set; }
    protected int MaxAir { get; set; } = 300;
    protected bool InWater { get; set; }
    public int Hearts { get; set; } = 0;
    public int Air { get; set; } = 300;
    private bool _firstTick = true;
    public string CloakUrl { get; set; }
    protected bool IsImmuneToFire { get; set; } = false;
    public DataSynchronizer DataSynchronizer { get; set; } = new();
    public float MinBrightness { get; set; } = 0.0F;
    private double _vehiclePitchDelta;
    private double _vehicleYawDelta;
    public bool IsPersistent { get; set; } = false;
    public int ChunkX { get; set; }
    public int ChunkSlice { get; set; }
    public int ChunkZ { get; set; }
    public int TrackedPosX { get; set; }
    public int TrackedPosY { get; set; }
    public int TrackedPosZ { get; set; }
    public bool IgnoreFrustumCheck { get; set; }
    private readonly SyncedProperty<byte> _flags;

    public Entity(IWorldContext world)
    {
        this.World = world;
        SetPosition(0.0D, 0.0D, 0.0D);
        _flags = DataSynchronizer.MakeProperty<byte>(0, 0);
    }

    public Vec3D Position => new Vec3D(X, Y, Z);

    public virtual void TeleportToTop()
    {
        if (World != null)
        {
            while (Y > 0.0D)
            {
                SetPosition(X, Y, Z);
                if (World.Entities.GetEntityCollisionsScratch(this, BoundingBox).Count == 0)
                {
                    break;
                }

                ++Y;
            }

            VelocityX = VelocityY = VelocityZ = 0.0D;
            Pitch = 0.0F;
        }
    }

    public virtual void MarkDead()
    {
        Dead = true;
    }

    protected virtual void SetBoundingBoxSpacing(float width, float height)
    {
        this.Width = width;
        this.Height = height;
    }

    protected void SetRotation(float yaw, float pitch)
    {
        this.Yaw = yaw % 360.0F;
        this.Pitch = pitch % 360.0F;
    }

    public void SetPosition(double x, double y, double z)
    {
        this.X = x;
        this.Y = y;
        this.Z = z;
        float halfWidth = Width / 2.0F;
        float height = this.Height;
        BoundingBox = new Box(x - (double)halfWidth, y - (double)StandingEyeHeight + (double)CameraOffset, z - (double)halfWidth, x + (double)halfWidth, y - (double)StandingEyeHeight + (double)CameraOffset + (double)height, z + (double)halfWidth);
    }

    public void ChangeLookDirection(float yaw, float pitch)
    {
        float oldPitch = this.Pitch;
        float oldYaw = this.Yaw;
        this.Yaw = (float)((double)this.Yaw + (double)yaw * 0.15D);
        this.Pitch = (float)((double)this.Pitch - (double)pitch * 0.15D);
        if (this.Pitch < -90.0F)
        {
            this.Pitch = -90.0F;
        }

        if (this.Pitch > 90.0F)
        {
            this.Pitch = 90.0F;
        }

        PrevPitch += this.Pitch - oldPitch;
        PrevYaw += this.Yaw - oldYaw;
    }

    public virtual void Tick()
    {
        BaseTick();
    }

    public virtual void BaseTick()
    {
        if (Vehicle != null && Vehicle.Dead)
        {
            Vehicle = null;
        }

        ++Age;
        PrevHorizontalSpeed = HorizontalSpeed;
        PrevX = X;
        PrevY = Y;
        PrevZ = Z;
        PrevPitch = Pitch;
        PrevYaw = Yaw;
        if (CheckWaterCollisions())
        {
            if (!InWater && !_firstTick)
            {
                float var1 = MathHelper.Sqrt(VelocityX * VelocityX * (double)0.2F + VelocityY * VelocityY + VelocityZ * VelocityZ * (double)0.2F) * 0.2F;
                if (var1 > 1.0F)
                {
                    var1 = 1.0F;
                }

                World.Broadcaster.PlaySoundAtEntity(this, "random.splash", var1, 1.0F + (Random.NextFloat() - Random.NextFloat()) * 0.4F);
                float var2 = (float)MathHelper.Floor(BoundingBox.MinY);

                int var3;
                float var4;
                float var5;
                for (var3 = 0; (float)var3 < 1.0F + Width * 20.0F; ++var3)
                {
                    var4 = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    var5 = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    World.Broadcaster.AddParticle("bubble", X + (double)var4, (double)(var2 + 1.0F), Z + (double)var5, VelocityX, VelocityY - (double)(Random.NextFloat() * 0.2F), VelocityZ);
                }

                for (var3 = 0; (float)var3 < 1.0F + Width * 20.0F; ++var3)
                {
                    var4 = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    var5 = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    World.Broadcaster.AddParticle("splash", X + (double)var4, (double)(var2 + 1.0F), Z + (double)var5, VelocityX, VelocityY, VelocityZ);
                }
            }

            FallDistance = 0.0F;
            InWater = true;
            FireTicks = 0;
        }
        else
        {
            InWater = false;
        }

        if (World.IsRemote)
        {
            FireTicks = 0;
        }
        else if (FireTicks > 0)
        {
            if (IsImmuneToFire)
            {
                FireTicks -= 4;
                if (FireTicks < 0)
                {
                    FireTicks = 0;
                }
            }
            else
            {
                if (FireTicks % 20 == 0)
                {
                    Damage((Entity)null, 1);
                }

                --FireTicks;
            }
        }

        if (IsTouchingLava())
        {
            SetOnFire();
        }

        if (Y < -64.0D)
        {
            TickInVoid();
        }

        if (!World.IsRemote)
        {
            SetFlag(0, FireTicks > 0);
            SetFlag(2, Vehicle != null);
        }

        _firstTick = false;
    }

    protected void SetOnFire()
    {
        if (!IsImmuneToFire)
        {
            Damage((Entity)null, 4);
            FireTicks = 600;
        }

    }

    protected virtual void TickInVoid()
    {
        MarkDead();
    }

    public bool GetEntitiesInside(double x, double y, double z)
    {
        Box box = BoundingBox.Offset(x, y, z);
        List<Box> entitiesInbound = World.Entities.GetEntityCollisionsScratch(this, box);
        return entitiesInbound.Count > 0 ? false : !World.Reader.IsMaterialInBox(box, m => m.IsFluid);
    }

    public virtual void Move(double x, double y, double z)
    {
        if (World.IsRemote && this is not EntityPlayer)
        {
            int minChunkX = MathHelper.Floor(BoundingBox.MinX) >> 4;
            int maxChunkX = MathHelper.Floor(BoundingBox.MaxX) >> 4;
            int minChunkZ = MathHelper.Floor(BoundingBox.MinZ) >> 4;
            int maxChunkZ = MathHelper.Floor(BoundingBox.MaxZ) >> 4;

            for (int chunkX = minChunkX; chunkX <= maxChunkX; ++chunkX)
            {
                for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; ++chunkZ)
                {
                    var chunk = World.ChunkHost.GetChunk(chunkX, chunkZ);
                    if (!chunk.Loaded)
                    {
                        VelocityX = VelocityY = VelocityZ = 0.0D;
                        return;
                    }
                }
            }
        }

        if (NoClip)
        {
            BoundingBox.Translate(x, y, z);
            this.X = (BoundingBox.MinX + BoundingBox.MaxX) / 2.0D;
            this.Y = BoundingBox.MinY + (double)StandingEyeHeight - (double)CameraOffset;
            this.Z = (BoundingBox.MinZ + BoundingBox.MaxZ) / 2.0D;
        }
        else
        {
            CameraOffset *= 0.4F;
            double var7 = this.X;
            double var9 = this.Z;
            if (Slowed)
            {
                Slowed = false;
                x *= 0.25D;
                y *= (double)0.05F;
                z *= 0.25D;
                VelocityX = 0.0D;
                VelocityY = 0.0D;
                VelocityZ = 0.0D;
            }

            double var11 = x;
            double var13 = y;
            double var15 = z;
            Box var17 = BoundingBox;
            bool var18 = OnGround && IsSneaking();
            if (var18)
            {
                double var19;
                for (var19 = 0.05D; x != 0.0D && World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Offset(x, -1.0D, 0.0D)).Count == 0; var11 = x)
                {
                    if (x < var19 && x >= -var19)
                    {
                        x = 0.0D;
                    }
                    else if (x > 0.0D)
                    {
                        x -= var19;
                    }
                    else
                    {
                        x += var19;
                    }
                }

                for (; z != 0.0D && World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Offset(0.0D, -1.0D, z)).Count == 0; var15 = z)
                {
                    if (z < var19 && z >= -var19)
                    {
                        z = 0.0D;
                    }
                    else if (z > 0.0D)
                    {
                        z -= var19;
                    }
                    else
                    {
                        z += var19;
                    }
                }
            }

            List<Box> entitiesInbound = World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Stretch(x, y, z));

            for (int var20 = 0; var20 < entitiesInbound.Count; ++var20)
            {
                y = entitiesInbound[var20].GetYOffset(BoundingBox, y);
            }

            BoundingBox.Translate(0.0D, y, 0.0D);
            if (!KeepVelocityOnCollision && var13 != y)
            {
                z = 0.0D;
                y = z;
                x = z;
            }

            bool var36 = OnGround || var13 != y && var13 < 0.0D;

            int i;
            for (i = 0; i < entitiesInbound.Count; ++i)
            {
                x = entitiesInbound[i].GetXOffset(BoundingBox, x);
            }

            BoundingBox.Translate(x, 0.0D, 0.0D);
            if (!KeepVelocityOnCollision && var11 != x)
            {
                z = 0.0D;
                y = z;
                x = z;
            }

            for (i = 0; i < entitiesInbound.Count; ++i)
            {
                z = entitiesInbound[i].GetZOffset(BoundingBox, z);
            }

            BoundingBox.Translate(0.0D, 0.0D, z);
            if (!KeepVelocityOnCollision && var15 != z)
            {
                z = 0.0D;
                y = z;
                x = z;
            }

            double var23;
            int var28;
            double var37;
            if (StepHeight > 0.0F && var36 && (var18 || CameraOffset < 0.05F) && (var11 != x || var15 != z))
            {
                var37 = x;
                var23 = y;
                double var25 = z;
                x = var11;
                y = (double)StepHeight;
                z = var15;
                Box var27 = BoundingBox;
                BoundingBox = var17;
                entitiesInbound = World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Stretch(var11, y, var15));

                for (var28 = 0; var28 < entitiesInbound.Count; ++var28)
                {
                    y = entitiesInbound[var28].GetYOffset(BoundingBox, y);
                }

                BoundingBox.Translate(0.0D, y, 0.0D);
                if (!KeepVelocityOnCollision && var13 != y)
                {
                    z = 0.0D;
                    y = z;
                    x = z;
                }

                for (var28 = 0; var28 < entitiesInbound.Count; ++var28)
                {
                    x = entitiesInbound[var28].GetXOffset(BoundingBox, x);
                }

                BoundingBox.Translate(x, 0.0D, 0.0D);
                if (!KeepVelocityOnCollision && var11 != x)
                {
                    z = 0.0D;
                    y = z;
                    x = z;
                }

                for (var28 = 0; var28 < entitiesInbound.Count; ++var28)
                {
                    z = entitiesInbound[var28].GetZOffset(BoundingBox, z);
                }

                BoundingBox.Translate(0.0D, 0.0D, z);
                if (!KeepVelocityOnCollision && var15 != z)
                {
                    z = 0.0D;
                    y = z;
                    x = z;
                }

                if (!KeepVelocityOnCollision && var13 != y)
                {
                    z = 0.0D;
                    y = z;
                    x = z;
                }
                else
                {
                    y = (double)(-StepHeight);

                    for (var28 = 0; var28 < entitiesInbound.Count; ++var28)
                    {
                        y = entitiesInbound[var28].GetYOffset(BoundingBox, y);
                    }

                    BoundingBox.Translate(0.0D, y, 0.0D);
                }

                if (var37 * var37 + var25 * var25 >= x * x + z * z)
                {
                    x = var37;
                    y = var23;
                    z = var25;
                    BoundingBox = var27;
                }
                else
                {
                    double var41 = BoundingBox.MinY - (double)((int)BoundingBox.MinY);
                    if (var41 > 0.0D)
                    {
                        CameraOffset = (float)((double)CameraOffset + var41 + 0.01D);
                    }
                }
            }

            this.X = (BoundingBox.MinX + BoundingBox.MaxX) / 2.0D;
            this.Y = BoundingBox.MinY + (double)StandingEyeHeight - (double)CameraOffset;
            this.Z = (BoundingBox.MinZ + BoundingBox.MaxZ) / 2.0D;
            HorizontalCollison = var11 != x || var15 != z;
            VerticalCollision = var13 != y;
            OnGround = var13 != y && var13 < 0.0D;
            HasCollided = HorizontalCollison || VerticalCollision;
            Fall(y, OnGround);
            if (var11 != x)
            {
                VelocityX = 0.0D;
            }

            if (var13 != y)
            {
                VelocityY = 0.0D;
            }

            if (var15 != z)
            {
                VelocityZ = 0.0D;
            }

            var37 = this.X - var7;
            var23 = this.Z - var9;
            int var26;
            int var38;
            int var39;
            if (BypassesSteppingEffects() && !var18 && Vehicle == null)
            {
                HorizontalSpeed = (float)((double)HorizontalSpeed + (double)MathHelper.Sqrt(var37 * var37 + var23 * var23) * 0.6D);

                if (OnGround)
                {
                    var38 = MathHelper.Floor(this.X);
                    var26 = MathHelper.Floor(this.Y - (double)0.2F - (double)StandingEyeHeight);
                    var39 = MathHelper.Floor(this.Z);
                    var28 = World.Reader.GetBlockId(var38, var26, var39);
                    if (World.Reader.GetBlockId(var38, var26 - 1, var39) == Block.Fence.id)
                    {
                        var28 = World.Reader.GetBlockId(var38, var26 - 1, var39);
                    }

                    if (HorizontalSpeed > (float)_nextStepSoundDistance && var28 > 0)
                    {
                        _nextStepSoundDistance = (int)HorizontalSpeed + 1;
                        BlockSoundGroup soundGroup = Block.Blocks[var28].SoundGroup;
                        if (World.Reader.GetBlockId(var38, var26 + 1, var39) == Block.Snow.id)
                        {
                            soundGroup = Block.Snow.SoundGroup;
                            World.Broadcaster.PlaySoundAtEntity(this, soundGroup.StepSound, soundGroup.Volume * 0.15F, soundGroup.Pitch);
                        }
                        else if (!Block.Blocks[var28].material.IsFluid)
                        {
                            World.Broadcaster.PlaySoundAtEntity(this, soundGroup.StepSound, soundGroup.Volume * 0.15F, soundGroup.Pitch);
                        }

                        Block.Blocks[var28].onSteppedOn(new OnEntityStepEvent(World, this, var38, var26, var39));
                    }
                }
            }

            var38 = MathHelper.Floor(BoundingBox.MinX + 0.001D);
            var26 = MathHelper.Floor(BoundingBox.MinY + 0.001D);
            var39 = MathHelper.Floor(BoundingBox.MinZ + 0.001D);
            var28 = MathHelper.Floor(BoundingBox.MaxX - 0.001D);
            int var40 = MathHelper.Floor(BoundingBox.MaxY - 0.001D);
            int var30 = MathHelper.Floor(BoundingBox.MaxZ - 0.001D);
            if (World.ChunkHost.IsRegionLoaded(var38, var26, var39, var28, var40, var30))
            {
                for (int var31 = var38; var31 <= var28; ++var31)
                {
                    for (int var32 = var26; var32 <= var40; ++var32)
                    {
                        for (int var33 = var39; var33 <= var30; ++var33)
                        {
                            int var34 = World.Reader.GetBlockId(var31, var32, var33);
                            if (var34 > 0)
                            {
                                Block.Blocks[var34].onEntityCollision(new OnEntityCollisionEvent(World, this, var31, var32, var33));
                            }
                        }
                    }
                }
            }

            bool var42 = IsWet();
            if (World.Reader.IsMaterialInBox(BoundingBox.Contract(0.001D, 0.001D, 0.001D), m => m == Material.Fire || m == Material.Lava))
            {
                Damage(1);
                if (!var42)
                {
                    ++FireTicks;
                    if (FireTicks == 0)
                    {
                        FireTicks = 300;
                    }
                }
            }
            else if (FireTicks <= 0)
            {
                FireTicks = -FireImmunityTicks;
            }

            if (var42 && FireTicks > 0)
            {
                World.Broadcaster.PlaySoundAtEntity(this, "random.fizz", 0.7F, 1.6F + (Random.NextFloat() - Random.NextFloat()) * 0.4F);
                FireTicks = -FireImmunityTicks;
            }

        }
    }

    protected virtual bool BypassesSteppingEffects()
    {
        return true;
    }

    protected virtual void Fall(double fallDistance, bool onGround)
    {
        if (onGround)
        {
            if (this.FallDistance > 0.0F)
            {
                OnLanding(this.FallDistance);
                this.FallDistance = 0.0F;
            }
        }
        else if (fallDistance < 0.0D)
        {
            this.FallDistance = (float)((double)this.FallDistance - fallDistance);
        }

    }

    public virtual Box? GetBoundingBox()
    {
        return null;
    }

    protected virtual void Damage(int var1)
    {
        if (!IsImmuneToFire)
        {
            Damage((Entity)null, var1);
        }

    }

    protected virtual void OnLanding(float fallDistance)
    {
        if (Passenger != null)
        {
            Passenger.OnLanding(fallDistance);
        }

    }

    public bool IsWet()
    {
        return InWater || World.Environment.IsRainingAt(MathHelper.Floor(X), MathHelper.Floor(Y), MathHelper.Floor(Z));
    }

    public virtual bool IsInWater()
    {
        return InWater;
    }

    public virtual bool CheckWaterCollisions()
    {
        return World.Reader.UpdateMovementInFluid(BoundingBox.Expand(0.0D, (double)-0.4F, 0.0D).Contract(0.001D, 0.001D, 0.001D), Material.Water, this);
    }

    public bool IsInFluid(Material var1)
    {
        double var2 = Y + (double)GetEyeHeight();
        int var4 = MathHelper.Floor(X);
        int var5 = MathHelper.Floor((float)MathHelper.Floor(var2));
        int var6 = MathHelper.Floor(Z);
        int var7 = World.Reader.GetBlockId(var4, var5, var6);
        if (var7 != 0 && Block.Blocks[var7].material == var1)
        {
            float var8 = BlockFluid.getFluidHeightFromMeta(World.Reader.GetBlockMeta(var4, var5, var6)) - 1.0F / 9.0F;
            float var9 = (float)(var5 + 1) - var8;
            return var2 < (double)var9;
        }
        else
        {
            return false;
        }
    }

    public virtual float GetEyeHeight()
    {
        return 0.0F;
    }

    public bool IsTouchingLava()
    {
        return World.Reader.IsMaterialInBox(BoundingBox.Expand(-0.1F, -0.4F, -0.1F), m => m == Material.Lava);
    }

    public void MoveNonSolid(float strafe, float forward, float speed)
    {
        float inputLength = MathHelper.Sqrt(strafe * strafe + forward * forward);
        if (inputLength >= 0.01F)
        {
            if (inputLength < 1.0F)
            {
                inputLength = 1.0F;
            }

            inputLength = speed / inputLength;
            strafe *= inputLength;
            forward *= inputLength;
            float sinYaw = MathHelper.Sin(Yaw * (float)System.Math.PI / 180.0F);
            float cosYaw = MathHelper.Cos(Yaw * (float)System.Math.PI / 180.0F);
            VelocityX += (double)(strafe * cosYaw - forward * sinYaw);
            VelocityZ += (double)(forward * cosYaw + strafe * sinYaw);
        }
    }

    public virtual float GetBrightnessAtEyes(float var1)
    {
        int var2 = MathHelper.Floor(X);
        double var3 = (BoundingBox.MaxY - BoundingBox.MinY) * 0.66D;
        int var5 = MathHelper.Floor(Y - (double)StandingEyeHeight + var3);
        int var6 = MathHelper.Floor(Z);

        int minX = MathHelper.Floor(BoundingBox.MinX);
        int minY = MathHelper.Floor(BoundingBox.MinY);
        int minZ = MathHelper.Floor(BoundingBox.MinZ);
        int maxX = MathHelper.Floor(BoundingBox.MaxX);
        int maxY = MathHelper.Floor(BoundingBox.MaxY);
        int maxZ = MathHelper.Floor(BoundingBox.MaxZ);

        minY = Math.Min(127, Math.Max(0, minY));
        maxY = Math.Min(127, Math.Max(0, maxY));

        if (World.ChunkHost.IsRegionLoaded(minX, minY, minZ, maxX, maxY, maxZ))
        {
            float var7 = World.Lighting.GetLuminance(var2, var5, var6);
            if (var7 < MinBrightness)
            {
                var7 = MinBrightness;
            }

            return var7;
        }
        else
        {
            return MinBrightness;
        }
    }

    public virtual void SetWorld(IWorldContext world)
    {
        this.World = world;
    }

    public void SetPositionAndAngles(double x, double y, double z, float yaw, float pitch)
    {
        PrevX = this.X = x;
        PrevY = this.Y = y;
        PrevZ = this.Z = z;
        PrevYaw = this.Yaw = yaw;
        PrevPitch = this.Pitch = pitch;
        CameraOffset = 0.0F;
        double var9 = (double)(PrevYaw - yaw);
        if (var9 < -180.0D)
        {
            PrevYaw += 360.0F;
        }

        if (var9 >= 180.0D)
        {
            PrevYaw -= 360.0F;
        }

        SetPosition(this.X, this.Y, this.Z);
        SetRotation(yaw, pitch);
    }

    public void SetPositionAndAnglesKeepPrevAngles(double x, double y, double z, float yaw, float pitch)
    {
        LastTickX = PrevX = this.X = x;
        LastTickY = PrevY = this.Y = y + (double)StandingEyeHeight;
        LastTickZ = PrevZ = this.Z = z;
        this.Yaw = yaw;
        this.Pitch = pitch;
        SetPosition(this.X, this.Y, this.Z);
    }

    public float GetDistance(Entity entity)
    {
        float var2 = (float)(X - entity.X);
        float var3 = (float)(Y - entity.Y);
        float var4 = (float)(Z - entity.Z);
        return MathHelper.Sqrt(var2 * var2 + var3 * var3 + var4 * var4);
    }

    public double GetSquaredDistance(double var1, double var3, double var5)
    {
        double var7 = X - var1;
        double var9 = Y - var3;
        double var11 = Z - var5;
        return var7 * var7 + var9 * var9 + var11 * var11;
    }

    public double GetDistance(double var1, double var3, double var5)
    {
        double var7 = X - var1;
        double var9 = Y - var3;
        double var11 = Z - var5;
        return (double)MathHelper.Sqrt(var7 * var7 + var9 * var9 + var11 * var11);
    }

    public double GetSquaredDistance(Entity entity)
    {
        double var2 = X - entity.X;
        double var4 = Y - entity.Y;
        double var6 = Z - entity.Z;
        return var2 * var2 + var4 * var4 + var6 * var6;
    }

    public virtual void OnPlayerInteraction(EntityPlayer player)
    {
    }

    public virtual void OnCollision(Entity entity)
    {
        if (entity.Passenger != this && entity.Vehicle != this)
        {
            double var2 = entity.X - X;
            double var4 = entity.Z - Z;
            double var6 = Math.Max(Math.Abs(var2), Math.Abs(var4));
            if (var6 >= (double)0.01F)
            {
                var6 = (double)MathHelper.Sqrt(var6);
                var2 /= var6;
                var4 /= var6;
                double var8 = 1.0D / var6;
                if (var8 > 1.0D)
                {
                    var8 = 1.0D;
                }

                var2 *= var8;
                var4 *= var8;
                var2 *= (double)0.05F;
                var4 *= (double)0.05F;
                var2 *= (double)(1.0F - PushSpeedReduction);
                var4 *= (double)(1.0F - PushSpeedReduction);
                const double maxHorizontalImpulsePerCollision = 0.05D;
                const double maxHorizontalSpeed = 0.05D;
                if (var2 > maxHorizontalImpulsePerCollision) var2 = maxHorizontalImpulsePerCollision;
                else if (var2 < -maxHorizontalImpulsePerCollision) var2 = -maxHorizontalImpulsePerCollision;

                if (var4 > maxHorizontalImpulsePerCollision) var4 = maxHorizontalImpulsePerCollision;
                else if (var4 < -maxHorizontalImpulsePerCollision) var4 = -maxHorizontalImpulsePerCollision;

                double impulseMag = MathHelper.Sqrt(var2 * var2 + var4 * var4);
                if (impulseMag > maxHorizontalImpulsePerCollision)
                {
                    double s = maxHorizontalImpulsePerCollision / impulseMag;
                    var2 *= s;
                    var4 *= s;
                }

                AddVelocity(-var2, 0.0D, -var4);
                entity.AddVelocity(var2, 0.0D, var4);

                double speedThis = MathHelper.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
                if (speedThis > maxHorizontalSpeed)
                {
                    double s = maxHorizontalSpeed / speedThis;
                    VelocityX *= s;
                    VelocityZ *= s;
                }

                double speedOther = MathHelper.Sqrt(entity.VelocityX * entity.VelocityX + entity.VelocityZ * entity.VelocityZ);
                if (speedOther > maxHorizontalSpeed)
                {
                    double s = maxHorizontalSpeed / speedOther;
                    entity.VelocityX *= s;
                    entity.VelocityZ *= s;
                }
            }

        }
    }

    public virtual void AddVelocity(double var1, double var3, double var5)
    {
        VelocityX += var1;
        VelocityY += var3;
        VelocityZ += var5;
    }

    protected void ScheduleVelocityUpdate()
    {
        VelocityModified = true;
    }

    public virtual bool Damage(Entity? entity, int amount)
    {
        ScheduleVelocityUpdate();
        return false;
    }

    public virtual bool IsCollidable()
    {
        return false;
    }

    public virtual bool IsPushable()
    {
        return false;
    }

    public virtual void UpdateKilledAchievement(Entity entity, int var2)
    {
    }

    public virtual bool ShouldRender(Vec3D var1)
    {
        double var2 = X - var1.x;
        double var4 = Y - var1.y;
        double var6 = Z - var1.z;
        double var8 = var2 * var2 + var4 * var4 + var6 * var6;
        return ShouldRender(var8);
    }

    public virtual bool ShouldRender(double var1)
    {
        double var3 = BoundingBox.AverageEdgeLength;
        var3 *= 64.0D * RenderDistanceWeight;
        return var1 < var3 * var3;
    }

    public virtual string GetTexture()
    {
        return null;
    }

    public bool SaveSelfNbt(NBTTagCompound nbt)
    {
        string var2 = GetRegistryEntry();
        if (!Dead && var2 != null)
        {
            nbt.SetString("id", var2);
            Write(nbt);
            return true;
        }
        else
        {
            return false;
        }
    }

    public void Write(NBTTagCompound nbt)
    {
        nbt.SetTag("Pos", NewDoubleNBTList(X, Y + (double)CameraOffset, Z));
        nbt.SetTag("Motion", NewDoubleNBTList(VelocityX, VelocityY, VelocityZ));
        nbt.SetTag("Rotation", NewFloatNBTList(Yaw, Pitch));
        nbt.SetFloat("FallDistance", FallDistance);
        nbt.SetShort("Fire", (short)FireTicks);
        nbt.SetShort("Air", (short)Air);
        nbt.SetBoolean("OnGround", OnGround);
        WriteNbt(nbt);
    }

    public void Read(NBTTagCompound nbt)
    {
        NBTTagList var2 = nbt.GetTagList("Pos");
        NBTTagList var3 = nbt.GetTagList("Motion");
        NBTTagList var4 = nbt.GetTagList("Rotation");
        VelocityX = ((NBTTagDouble)var3.TagAt(0)).Value;
        VelocityY = ((NBTTagDouble)var3.TagAt(1)).Value;
        VelocityZ = ((NBTTagDouble)var3.TagAt(2)).Value;
        if (System.Math.Abs(VelocityX) > 10.0D)
        {
            VelocityX = 0.0D;
        }

        if (System.Math.Abs(VelocityY) > 10.0D)
        {
            VelocityY = 0.0D;
        }

        if (System.Math.Abs(VelocityZ) > 10.0D)
        {
            VelocityZ = 0.0D;
        }

        PrevX = LastTickX = X = ((NBTTagDouble)var2.TagAt(0)).Value;
        PrevY = LastTickY = Y = ((NBTTagDouble)var2.TagAt(1)).Value;
        PrevZ = LastTickZ = Z = ((NBTTagDouble)var2.TagAt(2)).Value;
        PrevYaw = Yaw = ((NBTTagFloat)var4.TagAt(0)).Value;
        PrevPitch = Pitch = ((NBTTagFloat)var4.TagAt(1)).Value;
        FallDistance = nbt.GetFloat("FallDistance");
        FireTicks = nbt.GetShort("Fire");
        Air = nbt.GetShort("Air");
        OnGround = nbt.GetBoolean("OnGround");
        SetPosition(X, Y, Z);
        SetRotation(Yaw, Pitch);
        ReadNbt(nbt);
    }

    protected string? GetRegistryEntry()
    {
        return Type?.Id;
    }

    public abstract void ReadNbt(NBTTagCompound nbt);

    public abstract void WriteNbt(NBTTagCompound nbt);

    protected static NBTTagList NewDoubleNBTList(params double[] var1)
    {
        NBTTagList var2 = new();
        double[] var3 = var1;
        int var4 = var1.Length;

        for (int var5 = 0; var5 < var4; ++var5)
        {
            double var6 = var3[var5];
            var2.SetTag(new NBTTagDouble(var6));
        }

        return var2;
    }

    protected static NBTTagList NewFloatNBTList(params float[] var1)
    {
        NBTTagList var2 = new();
        float[] var3 = var1;
        int var4 = var1.Length;

        for (int var5 = 0; var5 < var4; ++var5)
        {
            float var6 = var3[var5];
            var2.SetTag(new NBTTagFloat(var6));
        }

        return var2;
    }

    public virtual float GetShadowRadius()
    {
        return Height / 2.0F;
    }

    public EntityItem DropItem(int var1, int var2)
    {
        return DropItem(var1, var2, 0.0F);
    }

    public EntityItem DropItem(int var1, int var2, float var3)
    {
        return DropItem(new ItemStack(var1, var2, 0), var3);
    }

    public EntityItem DropItem(ItemStack var1, float var2)
    {
        EntityItem var3 = new EntityItem(World, X, Y + (double)var2, Z, var1);
        var3.delayBeforeCanPickup = 10;
        World.SpawnEntity(var3);
        return var3;
    }

    public virtual bool IsAlive()
    {
        return !Dead;
    }

    public virtual bool IsInsideWall()
    {
        for (int var1 = 0; var1 < 8; ++var1)
        {
            float var2 = ((float)((var1 >> 0) % 2) - 0.5F) * Width * 0.9F;
            float var3 = ((float)((var1 >> 1) % 2) - 0.5F) * 0.1F;
            float var4 = ((float)((var1 >> 2) % 2) - 0.5F) * Width * 0.9F;
            int var5 = MathHelper.Floor(X + (double)var2);
            int var6 = MathHelper.Floor(Y + (double)GetEyeHeight() + (double)var3);
            int var7 = MathHelper.Floor(Z + (double)var4);
            if (World.Reader.ShouldSuffocate(var5, var6, var7))
            {
                return true;
            }
        }

        return false;
    }

    public virtual bool Interact(EntityPlayer player)
    {
        return false;
    }

    public virtual Box? GetCollisionAgainstShape(Entity entity)
    {
        return null;
    }

    public virtual void TickRiding()
    {
        if (Vehicle.Dead)
        {
            Vehicle = null;
        }
        else
        {
            VelocityX = 0.0D;
            VelocityY = 0.0D;
            VelocityZ = 0.0D;
            Tick();
            if (Vehicle != null)
            {
                Vehicle.UpdatePassengerPosition();
                _vehicleYawDelta += (double)(Vehicle.Yaw - Vehicle.PrevYaw);

                for (_vehiclePitchDelta += (double)(Vehicle.Pitch - Vehicle.PrevPitch); _vehicleYawDelta >= 180.0D; _vehicleYawDelta -= 360.0D)
                {
                }

                while (_vehicleYawDelta < -180.0D)
                {
                    _vehicleYawDelta += 360.0D;
                }

                while (_vehiclePitchDelta >= 180.0D)
                {
                    _vehiclePitchDelta -= 360.0D;
                }

                while (_vehiclePitchDelta < -180.0D)
                {
                    _vehiclePitchDelta += 360.0D;
                }

                double var1 = _vehicleYawDelta * 0.5D;
                double var3 = _vehiclePitchDelta * 0.5D;
                float var5 = 10.0F;
                if (var1 > (double)var5)
                {
                    var1 = (double)var5;
                }

                if (var1 < (double)(-var5))
                {
                    var1 = (double)(-var5);
                }

                if (var3 > (double)var5)
                {
                    var3 = (double)var5;
                }

                if (var3 < (double)(-var5))
                {
                    var3 = (double)(-var5);
                }

                _vehicleYawDelta -= var1;
                _vehiclePitchDelta -= var3;
                Yaw = (float)((double)Yaw + var1);
                Pitch = (float)((double)Pitch + var3);
            }
        }
    }

    public virtual void UpdatePassengerPosition()
    {
        Passenger.SetPosition(X, Y + GetPassengerRidingHeight() + Passenger.GetStandingEyeHeight(), Z);
    }

    public virtual double GetStandingEyeHeight()
    {
        return (double)StandingEyeHeight;
    }

    public virtual double GetPassengerRidingHeight()
    {
        return (double)Height * 0.75D;
    }

    public virtual void SetVehicle(Entity entity)
    {
        _vehiclePitchDelta = 0.0D;
        _vehicleYawDelta = 0.0D;
        if (entity == null)
        {
            if (Vehicle != null)
            {
                SetPositionAndAnglesKeepPrevAngles(Vehicle.X, Vehicle.BoundingBox.MinY + (double)Vehicle.Height, Vehicle.Z, Yaw, Pitch);
                Vehicle.Passenger = null;
            }

            Vehicle = null;
        }
        else if (Vehicle == entity)
        {
            Vehicle.Passenger = null;
            Vehicle = null;
            SetPositionAndAnglesKeepPrevAngles(entity.X, entity.BoundingBox.MinY + (double)entity.Height, entity.Z, Yaw, Pitch);
        }
        else
        {
            if (Vehicle != null)
            {
                Vehicle.Passenger = null;
            }

            if (entity.Passenger != null)
            {
                entity.Passenger.Vehicle = null;
            }

            Vehicle = entity;
            entity.Passenger = this;
        }
    }

    public virtual void SetPositionAndAnglesAvoidEntities(double x, double y, double z, float var7, float var8, int var9)
    {
        SetPosition(x, y, z);
        SetRotation(var7, var8);
        var var10 = World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Contract(1.0D / 32.0D, 0.0D, 1.0D / 32.0D));
        if (var10.Count > 0)
        {
            double var11 = 0.0D;

            for (int var13 = 0; var13 < var10.Count; ++var13)
            {
                Box var14 = var10[var13];
                if (var14.MaxY > var11)
                {
                    var11 = var14.MaxY;
                }
            }

            y += var11 - BoundingBox.MinY;
            SetPosition(x, y, z);
        }

    }

    public virtual float GetTargetingMargin()
    {
        return 0.1F;
    }

    public virtual Vec3D? GetLookVector()
    {
        return null;
    }

    public virtual void TickPortalCooldown()
    {
    }

    public virtual void SetVelocityClient(double var1, double var3, double var5)
    {
        VelocityX = var1;
        VelocityY = var3;
        VelocityZ = var5;
    }

    public virtual void ProcessServerEntityStatus(sbyte var1)
    {
    }

    public virtual void AnimateHurt()
    {
    }

    public virtual void UpdateCloak()
    {
    }

    public virtual void SetEquipmentStack(int var1, int var2, int var3)
    {
    }

    public bool IsOnFire()
    {
        return FireTicks > 0 || GetFlag(0);
    }

    public bool HasVehicle()
    {
        return Vehicle != null || GetFlag(2);
    }

    public virtual ItemStack[] GetEquipment()
    {
        return null;
    }

    public virtual bool IsSneaking()
    {
        return GetFlag(1);
    }

    public void SetSneaking(bool sneaking)
    {
        SetFlag(1, sneaking);
    }

    protected bool GetFlag(int index)
    {
        return (_flags.Value & (1 << index)) != 0;
    }

    protected void SetFlag(int index, bool value)
    {
        byte oldValue = _flags.Value;
        byte newValue;
        if (value)
        {
            newValue = (byte)(oldValue | (1 << index));
        }
        else
        {
            newValue = (byte)(oldValue & ~(1 << index));
        }

        _flags.Value = newValue;
    }

    public virtual void OnStruckByLightning(EntityLightningBolt bolt)
    {
        Damage(5);
        ++FireTicks;
        if (FireTicks == 0)
        {
            FireTicks = 300;
        }

    }

    public virtual void OnKillOther(EntityLiving var1)
    {
    }

    protected virtual bool PushOutOfBlocks(double x, double y, double z)
    {
        // Only players should attempt "push out of blocks".
        if (this is not EntityPlayer)
        {
            return false;
        }

        int floorX = MathHelper.Floor(x);
        int floorY = MathHelper.Floor(y);
        int floorZ = MathHelper.Floor(z);
        double fracX = x - floorX;
        double fracY = y - floorY;
        double fracZ = z - floorZ;
        if (World.Reader.ShouldSuffocate(floorX, floorY, floorZ))
        {
            bool canPushWest = !World.Reader.ShouldSuffocate(floorX - 1, floorY, floorZ);
            bool canPushEast = !World.Reader.ShouldSuffocate(floorX + 1, floorY, floorZ);
            bool canPushDown = !World.Reader.ShouldSuffocate(floorX, floorY - 1, floorZ);
            bool canPushUp = !World.Reader.ShouldSuffocate(floorX, floorY + 1, floorZ);
            bool canPushNorth = !World.Reader.ShouldSuffocate(floorX, floorY, floorZ - 1);
            bool canPushSouth = !World.Reader.ShouldSuffocate(floorX, floorY, floorZ + 1);
            int pushDirection = -1;
            double closestEdgeDistance = 9999.0D;
            if (canPushWest && fracX < closestEdgeDistance)
            {
                closestEdgeDistance = fracX;
                pushDirection = 0;
            }

            if (canPushEast && 1.0D - fracX < closestEdgeDistance)
            {
                closestEdgeDistance = 1.0D - fracX;
                pushDirection = 1;
            }

            if (canPushDown && fracY < closestEdgeDistance)
            {
                closestEdgeDistance = fracY;
                pushDirection = 2;
            }

            if (canPushUp && 1.0D - fracY < closestEdgeDistance)
            {
                closestEdgeDistance = 1.0D - fracY;
                pushDirection = 3;
            }

            if (canPushNorth && fracZ < closestEdgeDistance)
            {
                closestEdgeDistance = fracZ;
                pushDirection = 4;
            }

            if (canPushSouth && 1.0D - fracZ < closestEdgeDistance)
            {
                closestEdgeDistance = 1.0D - fracZ;
                pushDirection = 5;
            }

            float pushStrength = Random.NextFloat() * 0.2F + 0.1F;
            if (pushDirection == 0)
            {
                VelocityX = (double)(-pushStrength);
            }

            if (pushDirection == 1)
            {
                VelocityX = (double)pushStrength;
            }

            if (pushDirection == 2)
            {
                VelocityY = (double)(-pushStrength);
            }

            if (pushDirection == 3)
            {
                VelocityY = (double)pushStrength;
            }

            if (pushDirection == 4)
            {
                VelocityZ = (double)(-pushStrength);
            }

            if (pushDirection == 5)
            {
                VelocityZ = (double)pushStrength;
            }
        }

        return false;
    }

    public override bool Equals(object other)
    {
        return other is Entity e && e.ID == ID;
    }

    public override int GetHashCode()
    {
        return ID;
    }
}
