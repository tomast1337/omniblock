using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Materials;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities;

/// <summary>
///     Where an entity is and how it gets there: the bounding box it occupies, the collision passes
///     that trim a step against everything in the way, and what the resulting position implies
///     (falling, footsteps, block callbacks, fire and water).
/// </summary>
public abstract partial class Entity
{
    private int _nextStepSoundDistance = 1;

    /// <summary>
    ///     Keep moving up until there's no collision.
    /// </summary>
    /// <remarks>
    ///     Note that the Pitch will be reset to 0, and the Motion
    ///     will be fully zeroed, so the entity might fall for a bit
    ///     if the position was off at the start.
    /// </remarks>
    public virtual void TeleportToTop()
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

    protected internal virtual void SetBoundingBoxSpacing(float width, float height)
    {
        Width = width;
        Height = height;
    }

    protected internal void SetRotation(float yaw, float pitch)
    {
        Yaw = yaw % 360.0F;
        Pitch = pitch % 360.0F;
    }

    public void SetPosition(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
        UpdateBoundingBox();
    }

    public void UpdateBoundingBox()
    {
        float halfWidth = Width / 2.0F;
        BoundingBox = new Box(X - halfWidth, Y - StandingEyeHeight + CameraOffset, Z - halfWidth, X + halfWidth, Y - StandingEyeHeight + CameraOffset + Height, Z + halfWidth);
    }

    public void SetPosition(double y)
    {
        Y = y;
        BoundingBox.MinY = y - StandingEyeHeight + CameraOffset;
        BoundingBox.MaxY = y - StandingEyeHeight + CameraOffset + Height;
    }

    /// <summary>
    ///     Change the current look direction, with capping the pitch.
    /// </summary>
    public void ChangeLookDirection(float yaw, float pitch)
    {
        float oldPitch = Pitch;
        float oldYaw = Yaw;
        Yaw = (float)(Yaw + yaw * 0.15D);
        Pitch = (float)(Pitch - pitch * 0.15D);
        if (Pitch < -90.0F)
        {
            Pitch = -90.0F;
        }

        if (Pitch > 90.0F)
        {
            Pitch = 90.0F;
        }

        PrevPitch += Pitch - oldPitch;
        PrevYaw += Yaw - oldYaw;
    }

    protected bool GetEntitiesInside(double x, double y, double z)
    {
        Box box = BoundingBox.Offset(x, y, z);
        List<Box> entitiesInbound = World.Entities.GetEntityCollisionsScratch(this, box);
        return entitiesInbound.Count <= 0 && !World.Reader.IsMaterialInBox(box, m => m.IsFluid);
    }

    /// <summary>
    ///     Move by a certain amount, making sure to handle collisions and the such.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <summary>
    ///     Steps the entity by the given displacement, trimmed against everything in the way, then
    ///     applies what the resulting position implies: fall distance, footsteps, block-collision
    ///     callbacks, fire and water.
    /// </summary>
    public virtual void Move(double dx, double dy, double dz)
    {
        // On the client an entity straddling an unloaded chunk has nothing to collide against, so it
        // stops where it is rather than falling through the gap. The local player is exempt: its own
        // movement is authoritative.
        if (World.IsRemote && this is not EntityPlayer && !IsFootprintLoaded())
        {
            VelocityX = VelocityY = VelocityZ = 0.0D;
            return;
        }

        if (NoClip)
        {
            BoundingBox.Translate(dx, dy, dz);
            SyncPositionToBoundingBox();
            return;
        }

        CameraOffset *= 0.4F;

        double startX = X;
        double startZ = Z;

        if (Slowed)
        {
            Slowed = false;
            dx *= 0.25D;
            dy *= 0.05F;
            dz *= 0.25D;
            VelocityX = 0.0D;
            VelocityY = 0.0D;
            VelocityZ = 0.0D;
        }

        Box boxBeforeMove = BoundingBox;
        bool sneakingOnGround = OnGround && IsSneaking();
        if (sneakingOnGround)
        {
            dx = ShortenStepOverLedge(dx, true);
            dz = ShortenStepOverLedge(dz, false);
        }

        // What was asked for, against which the resolved step is compared to detect a collision.
        double requestedX = dx;
        double requestedY = dy;
        double requestedZ = dz;

        List<Box> colliders = World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Stretch(dx, dy, dz));
        ResolveCollisions(colliders, ref dx, ref dy, ref dz);

        // Standing on something, or having just landed on it, is what allows a step up.
        bool canStepUp = OnGround || (requestedY != dy && requestedY < 0.0D);
        if (StepHeight > 0.0F
            && canStepUp
            && (sneakingOnGround || CameraOffset < 0.05F)
            && (requestedX != dx || requestedZ != dz))
        {
            TryStepUp(boxBeforeMove, requestedX, requestedZ, ref dx, ref dy, ref dz);
        }

        SyncPositionToBoundingBox();

        HorizontalCollision = requestedX != dx || requestedZ != dz;
        VerticalCollision = requestedY != dy;
        OnGround = requestedY != dy && requestedY < 0.0D;
        HasCollided = HorizontalCollision || VerticalCollision;
        Fall(dy, OnGround);

        if (requestedX != dx)
        {
            VelocityX = 0.0D;
        }

        if (requestedY != dy)
        {
            VelocityY = 0.0D;
        }

        if (requestedZ != dz)
        {
            VelocityZ = 0.0D;
        }

        if (BypassesSteppingEffects() && !sneakingOnGround && Vehicle == null)
        {
            AccumulateWalkDistance(X - startX, Z - startZ);
        }

        NotifyBlocksOfCollision();
        ApplyFireAndWater();
    }

    /// <summary>Whether every chunk this entity's box overlaps is loaded.</summary>
    private bool IsFootprintLoaded()
    {
        int minChunkX = MathHelper.Floor(BoundingBox.MinX) >> 4;
        int maxChunkX = MathHelper.Floor(BoundingBox.MaxX) >> 4;
        int minChunkZ = MathHelper.Floor(BoundingBox.MinZ) >> 4;
        int maxChunkZ = MathHelper.Floor(BoundingBox.MaxZ) >> 4;

        for (int chunkX = minChunkX; chunkX <= maxChunkX; ++chunkX)
        {
            for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; ++chunkZ)
            {
                if (!World.ChunkHost.GetChunk(chunkX, chunkZ).Loaded)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Recentres the entity on its box, which is what the collision passes actually move.</summary>
    private void SyncPositionToBoundingBox()
    {
        X = (BoundingBox.MinX + BoundingBox.MaxX) / 2.0D;
        Y = BoundingBox.MinY + StandingEyeHeight - CameraOffset;
        Z = (BoundingBox.MinZ + BoundingBox.MaxZ) / 2.0D;
    }

    /// <summary>
    ///     Shortens a sneaking entity's step until there is ground under where it would land, or
    ///     until nothing is left of the step. This is what stops a sneaking player walking off a
    ///     ledge.
    /// </summary>
    private double ShortenStepOverLedge(double step, bool alongX)
    {
        const double edgeStep = 0.05D;

        while (step != 0.0D
               && World.Entities.GetEntityCollisionsScratch(
                   this,
                   BoundingBox.Offset(alongX ? step : 0.0D, -1.0D, alongX ? 0.0D : step)).Count == 0)
        {
            if (step < edgeStep && step >= -edgeStep)
            {
                step = 0.0D;
            }
            else if (step > 0.0D)
            {
                step -= edgeStep;
            }
            else
            {
                step += edgeStep;
            }
        }

        return step;
    }

    /// <summary>
    ///     Trims a step against everything in the way, one axis at a time, moving the box as it goes.
    ///     Y runs first because whether the entity landed on something decides what the horizontal
    ///     passes are stepping off.
    /// </summary>
    private void ResolveCollisions(List<Box> colliders, ref double dx, ref double dy, ref double dz)
    {
        for (int i = 0; i < colliders.Count; ++i)
        {
            dy = colliders[i].GetYOffset(BoundingBox, dy);
        }

        BoundingBox.Translate(0.0D, dy, 0.0D);

        for (int i = 0; i < colliders.Count; ++i)
        {
            dx = colliders[i].GetXOffset(BoundingBox, dx);
        }

        BoundingBox.Translate(dx, 0.0D, 0.0D);

        for (int i = 0; i < colliders.Count; ++i)
        {
            dz = colliders[i].GetZOffset(BoundingBox, dz);
        }

        BoundingBox.Translate(0.0D, 0.0D, dz);
    }

    /// <summary>
    ///     Replays the blocked step from a step-height above the start, then settles back down onto
    ///     whatever it cleared. Whichever of the two routes travelled further horizontally is kept,
    ///     so an entity only climbs when climbing actually got it somewhere.
    /// </summary>
    private void TryStepUp(Box boxBeforeMove, double requestedX, double requestedZ, ref double dx, ref double dy, ref double dz)
    {
        double flatX = dx;
        double flatY = dy;
        double flatZ = dz;
        Box boxAfterFlatMove = BoundingBox;

        dx = requestedX;
        dy = StepHeight;
        dz = requestedZ;
        BoundingBox = boxBeforeMove;

        List<Box> colliders = World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Stretch(requestedX, dy, requestedZ));
        ResolveCollisions(colliders, ref dx, ref dy, ref dz);

        dy = -StepHeight;

        for (int i = 0; i < colliders.Count; ++i)
        {
            dy = colliders[i].GetYOffset(BoundingBox, dy);
        }

        BoundingBox.Translate(0.0D, dy, 0.0D);

        if (flatX * flatX + flatZ * flatZ >= dx * dx + dz * dz)
        {
            dx = flatX;
            dy = flatY;
            dz = flatZ;
            BoundingBox = boxAfterFlatMove;
            return;
        }

        // Climbing snaps the body up a whole step; the camera is left behind and eases back up.
        double riseIntoBlock = BoundingBox.MinY - (int)BoundingBox.MinY;
        if (riseIntoBlock > 0.0D)
        {
            CameraOffset = (float)(CameraOffset + riseIntoBlock + 0.01D);
        }
    }

    /// <summary>
    ///     Adds this step to the walk distance and plays a footstep once enough of it has built up.
    ///     The block underfoot picks the sound: a fence is heard instead of whatever it stands on,
    ///     and snow covers whatever lies beneath it.
    /// </summary>
    private void AccumulateWalkDistance(double travelledX, double travelledZ)
    {
        HorizontalSpeed = (float)(HorizontalSpeed + MathHelper.Sqrt(travelledX * travelledX + travelledZ * travelledZ) * 0.6D);

        if (!OnGround)
        {
            return;
        }

        int blockX = MathHelper.Floor(X);
        int blockY = MathHelper.Floor(Y - 0.2F - StandingEyeHeight);
        int blockZ = MathHelper.Floor(Z);
        int blockId = World.Reader.GetBlockId(blockX, blockY, blockZ);

        if (World.Reader.GetBlockId(blockX, blockY - 1, blockZ) == BlockRegistry.Get("fence").Id)
        {
            blockId = World.Reader.GetBlockId(blockX, blockY - 1, blockZ);
        }

        if (HorizontalSpeed <= _nextStepSoundDistance || blockId <= 0)
        {
            return;
        }

        _nextStepSoundDistance = (int)HorizontalSpeed + 1;
        BlockSoundGroup soundGroup = Block.Blocks[blockId].SoundGroup;

        if (World.Reader.GetBlockId(blockX, blockY + 1, blockZ) == BlockRegistry.Get("snow").Id)
        {
            soundGroup = BlockRegistry.Get("snow").SoundGroup;
            World.Broadcaster.PlaySoundAtEntity(this, soundGroup.StepSound, soundGroup.Volume * 0.15F, soundGroup.Pitch);
        }
        else if (!Block.Blocks[blockId].Material.IsFluid)
        {
            World.Broadcaster.PlaySoundAtEntity(this, soundGroup.StepSound, soundGroup.Volume * 0.15F, soundGroup.Pitch);
        }

        Block.Blocks[blockId].onSteppedOn(new OnEntityStepEvent(World, this, blockX, blockY, blockZ));
    }

    /// <summary>Tells every block the box now overlaps that something is standing in it.</summary>
    private void NotifyBlocksOfCollision()
    {
        int minX = MathHelper.Floor(BoundingBox.MinX + 0.001D);
        int minY = MathHelper.Floor(BoundingBox.MinY + 0.001D);
        int minZ = MathHelper.Floor(BoundingBox.MinZ + 0.001D);
        int maxX = MathHelper.Floor(BoundingBox.MaxX - 0.001D);
        int maxY = MathHelper.Floor(BoundingBox.MaxY - 0.001D);
        int maxZ = MathHelper.Floor(BoundingBox.MaxZ - 0.001D);

        if (!World.ChunkHost.IsRegionLoaded(minX, minY, minZ, maxX, maxY, maxZ))
        {
            return;
        }

        for (int x = minX; x <= maxX; ++x)
        {
            for (int y = minY; y <= maxY; ++y)
            {
                for (int z = minZ; z <= maxZ; ++z)
                {
                    int blockId = World.Reader.GetBlockId(x, y, z);
                    if (blockId > 0)
                    {
                        Block.Blocks[blockId].OnEntityCollision(new OnEntityCollisionEvent(World, this, x, y, z));
                    }
                }
            }
        }
    }

    /// <summary>Standing in fire or lava burns; being wet puts it out with a fizz.</summary>
    private void ApplyFireAndWater()
    {
        bool wet = IsWet;

        if (World.Reader.IsMaterialInBox(BoundingBox.Contract(0.001D, 0.001D, 0.001D), m => m == Material.Fire || m == Material.Lava))
        {
            Damage(1);
            if (!wet)
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

        if (!wet || FireTicks <= 0)
        {
            return;
        }

        World.Broadcaster.PlaySoundAtEntity(this, "random.fizz", 0.7F, 1.6F + (Random.NextFloat() - Random.NextFloat()) * 0.4F);
        FireTicks = -FireImmunityTicks;
    }

    protected virtual bool BypassesSteppingEffects() => true;

    protected virtual void Fall(double fallDistance, bool onGround)
    {
        if (onGround)
        {
            if (!(FallDistance > 0.0F))
            {
                return;
            }

            OnLanding(FallDistance);
            FallDistance = 0.0F;
        }
        else if (fallDistance < 0.0D)
        {
            FallDistance = (float)(FallDistance - fallDistance);
        }
    }

    public virtual Box? GetBoundingBox() => null;

    protected virtual void OnLanding(float fallDistance) => Passenger?.OnLanding(fallDistance);

    public virtual bool CheckWaterCollisions() =>
        Physics?.CheckWaterCollisions(this)
        ?? World.Reader.UpdateMovementInFluid(BoundingBox.Expand(0.0D, -0.4F, 0.0D).Contract(0.001D, 0.001D, 0.001D), Material.Water, this);

    public bool IsInFluid(Material mat)
    {
        double eyeY = Y + EyeHeight;
        int floorX = MathHelper.Floor(X);
        int floorEyeY = MathHelper.Floor(MathHelper.Floor(eyeY));
        int floorZ = MathHelper.Floor(Z);
        int id = World.Reader.GetBlockId(floorX, floorEyeY, floorZ);
        if (id != 0 && Block.Blocks[id].Material == mat)
        {
            float fluidHeight = FluidMath.GetFluidHeightFromMeta(World.Reader.GetBlockMeta(floorX, floorEyeY, floorZ)) - 1.0F / 9.0F;
            float fluidSurfaceY = floorEyeY + 1 - fluidHeight;
            return eyeY < fluidSurfaceY;
        }

        return false;
    }

    protected internal void MoveNonSolid(float strafe, float forward, float speed)
    {
        float inputLength = MathHelper.Sqrt(strafe * strafe + forward * forward);
        if (!(inputLength >= 0.01F))
        {
            return;
        }

        if (inputLength < 1.0F)
        {
            inputLength = 1.0F;
        }

        inputLength = speed / inputLength;
        strafe *= inputLength;
        forward *= inputLength;
        float sinYaw = MathHelper.Sin(Yaw * (float)Math.PI / 180.0F);
        float cosYaw = MathHelper.Cos(Yaw * (float)Math.PI / 180.0F);
        VelocityX += strafe * cosYaw - forward * sinYaw;
        VelocityZ += forward * cosYaw + strafe * sinYaw;
    }

    public void SetPositionAndAngles(double x, double y, double z, float yaw, float pitch)
    {
        PrevX = X = x;
        PrevY = Y = y;
        PrevZ = Z = z;
        PrevYaw = Yaw = yaw;
        PrevPitch = Pitch = pitch;
        CameraOffset = 0.0F;
        double diff = PrevYaw - yaw;
        if (diff < -180.0D)
        {
            PrevYaw += 360.0F;
        }

        if (diff >= 180.0D)
        {
            PrevYaw -= 360.0F;
        }

        SetPosition(X, Y, Z);
        SetRotation(yaw, pitch);
    }

    public void SetPositionAndAnglesKeepPrevAngles(double x, double y, double z, float yaw, float pitch)
    {
        LastTickX = PrevX = X = x;
        LastTickY = PrevY = Y = y + StandingEyeHeight;
        LastTickZ = PrevZ = Z = z;
        Yaw = yaw;
        Pitch = pitch;
        SetPosition(X, Y, Z);
    }

    public double GetSquaredDistance(double x, double y, double z)
    {
        double diffX = X - x;
        double diffY = Y - y;
        double diffZ = Z - z;
        return diffX * diffX + diffY * diffY + diffZ * diffZ;
    }

    public double GetSquaredDistance(Entity entity) => GetSquaredDistance(entity.X, entity.Y, entity.Z);

    public double GetDistance(double x, double y, double z) => MathHelper.Sqrt(GetSquaredDistance(x, y, z));

    public float GetDistance(Entity entity) => (float)GetDistance(entity.X, entity.Y, entity.Z);

    public virtual void OnCollision(Entity entity)
    {
        if (Equals(entity.Passenger, this) || Equals(entity.Vehicle, this))
        {
            return;
        }

        double diffX = entity.X - X;
        double diffY = entity.Z - Z;
        double max = Math.Max(Math.Abs(diffX), Math.Abs(diffY));
        if (!(max >= 0.01F))
        {
            return;
        }

        max = MathHelper.Sqrt(max);
        diffX /= max;
        diffY /= max;
        double maxMulInverse = 1.0D / max;
        if (maxMulInverse > 1.0D)
        {
            maxMulInverse = 1.0D;
        }

        diffX *= maxMulInverse;
        diffY *= maxMulInverse;
        diffX *= 0.05F;
        diffY *= 0.05F;
        diffX *= 1.0F - PushSpeedReduction;
        diffY *= 1.0F - PushSpeedReduction;
        const double maxHorizontalImpulsePerCollision = 0.05D;
        const double maxHorizontalSpeed = 0.05D;
        if (diffX > maxHorizontalImpulsePerCollision)
        {
            diffX = maxHorizontalImpulsePerCollision;
        }
        else if (diffX < -maxHorizontalImpulsePerCollision)
        {
            diffX = -maxHorizontalImpulsePerCollision;
        }

        if (diffY > maxHorizontalImpulsePerCollision)
        {
            diffY = maxHorizontalImpulsePerCollision;
        }
        else if (diffY < -maxHorizontalImpulsePerCollision)
        {
            diffY = -maxHorizontalImpulsePerCollision;
        }

        double impulseMag = MathHelper.Sqrt(diffX * diffX + diffY * diffY);
        if (impulseMag > maxHorizontalImpulsePerCollision)
        {
            double s = maxHorizontalImpulsePerCollision / impulseMag;
            diffX *= s;
            diffY *= s;
        }

        AddVelocity(-diffX, 0.0D, -diffY);
        entity.AddVelocity(diffX, 0.0D, diffY);

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

    public virtual void AddVelocity(double vx, double vy, double vz)
    {
        VelocityX += vx;
        VelocityY += vy;
        VelocityZ += vz;
    }

    protected void ScheduleVelocityUpdate() => VelocityModified = true;

    public virtual bool IsInsideWall()
    {
        for (int i = 0; i < 8; ++i)
        {
            float offsetX = ((i >> 0) % 2 - 0.5F) * Width * 0.9F;
            float offsetY = ((i >> 1) % 2 - 0.5F) * 0.1F;
            float offsetZ = ((i >> 2) % 2 - 0.5F) * Width * 0.9F;
            int x = MathHelper.Floor(X + offsetX);
            int y = MathHelper.Floor(Y + EyeHeight + offsetY);
            int z = MathHelper.Floor(Z + offsetZ);
            if (World.Reader.ShouldSuffocate(x, y, z))
            {
                return true;
            }
        }

        return false;
    }

    public virtual Box? GetCollisionAgainstShape(Entity entity) => null;

    public void SetPositionAndAnglesAvoidEntities(int newPosRotationIncrements) => SetPositionAndAnglesAvoidEntities(Yaw, Pitch, newPosRotationIncrements);

    public void SetPositionAndAnglesAvoidEntities(float yaw, float pitch, int newPosRotationIncrements)
    {
        double posX = TrackedPosX / 32.0D;
        double posY = TrackedPosY / 32.0D;
        double posZ = TrackedPosZ / 32.0D;
        SetPositionAndAnglesAvoidEntities(posX, posY, posZ, yaw, pitch, newPosRotationIncrements);
    }

    public void SetPositionAndAnglesAvoidEntities(double x, double y, double z, int newPosRotationIncrements) => SetPositionAndAnglesAvoidEntities(x, y, z, Yaw, Pitch, newPosRotationIncrements);

    public virtual void SetPositionAndAnglesAvoidEntities(double x, double y, double z, float yaw, float pitch, int newPosRotationIncrements)
    {
        SetPosition(x, y, z);
        SetRotation(yaw, pitch);
        const double bound = 1.0D / 32.0D;
        double maxY = World.Entities.GetMaxYEntityCollision(this, BoundingBox.Contract(bound, 0.0D, bound));
        if (maxY <= 0)
        {
            return;
        }

        y += maxY - BoundingBox.MinY;
        SetPosition(y);
    }

    public virtual void SetVelocityClient(double vx, double vy, double vz)
    {
        VelocityX = vx;
        VelocityY = vy;
        VelocityZ = vz;
    }

    protected internal virtual bool PushOutOfBlocks(double x, double y, double z)
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
        if (!World.Reader.ShouldSuffocate(floorX, floorY, floorZ))
        {
            return false;
        }

        bool canPushWest = !World.Reader.ShouldSuffocate(floorX - 1, floorY, floorZ);
        bool canPushEast = !World.Reader.ShouldSuffocate(floorX + 1, floorY, floorZ);
        bool canPushDown = !World.Reader.ShouldSuffocate(floorX, floorY - 1, floorZ);
        bool canPushUp = !World.Reader.ShouldSuffocate(floorX, floorY + 1, floorZ);
        bool canPushNorth = !World.Reader.ShouldSuffocate(floorX, floorY, floorZ - 1);
        bool canPushSouth = !World.Reader.ShouldSuffocate(floorX, floorY, floorZ + 1);
        int pushDirection = -1;
        double closestEdgeDistance = double.MaxValue;
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
            pushDirection = 5;
        }

        float pushStrength = Random.NextFloat() * 0.2F + 0.1F;
        switch (pushDirection)
        {
            case 0:
                VelocityX = -pushStrength;
                break;
            case 1:
                VelocityX = pushStrength;
                break;
            case 2:
                VelocityY = -pushStrength;
                break;
            case 3:
                VelocityY = pushStrength;
                break;
            case 4:
                VelocityZ = -pushStrength;
                break;
            case 5:
                VelocityZ = pushStrength;
                break;
        }

        return false;
    }
}
