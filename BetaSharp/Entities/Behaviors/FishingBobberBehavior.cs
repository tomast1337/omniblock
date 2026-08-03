using BetaSharp.Blocks.Materials;
using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A fishing bobber: cast from a rod, it flies, floats, bobs under when something bites, and
///     hooks whatever it strikes on the way. Unlike other projectiles it stays tied to the player who
///     cast it: the rod reels it back with <see cref="Reel" />, and it removes itself as soon as the
///     angler stops holding a rod, dies, or walks too far away.
/// </summary>
public sealed class FishingBobberBehavior : IEntityTicker, IEntityPersistence, IEntityPhysics
{
    /// <summary>How hard a reel yanks whatever is on the end towards the angler.</summary>
    private const double PullStrength = 0.1D;

    private readonly StateHandle<EntityPlayer> _angler;
    private readonly int _biteDelay;
    private readonly int _biteDelayWhenRaining;

    private readonly Item _catch;

    /// <summary>What the bobber hooked, which it then rides until that entity dies.</summary>
    private readonly StateHandle<Entity> _hooked;

    private readonly StateHandle<bool> _inGround;
    private readonly StateHandle<int> _inTile;
    private readonly double _maxAnglerDistanceSquared;
    private readonly Item _rod;
    private readonly StateHandle<int> _shake;
    private readonly StateHandle<double> _syncedVelocityX;
    private readonly StateHandle<double> _syncedVelocityY;
    private readonly StateHandle<double> _syncedVelocityZ;

    private readonly StateHandle<int> _syncTicks;
    private readonly StateHandle<double> _targetPitch;
    private readonly StateHandle<double> _targetX;
    private readonly StateHandle<double> _targetY;
    private readonly StateHandle<double> _targetYaw;
    private readonly StateHandle<double> _targetZ;
    private readonly StateHandle<int> _ticksCatchable;
    private readonly StateHandle<int> _ticksInAir;
    private readonly StateHandle<int> _ticksInGround;
    private readonly StateHandle<int> _tileX;
    private readonly StateHandle<int> _tileY;
    private readonly StateHandle<int> _tileZ;

    public FishingBobberBehavior(EntityStateLayout layout, Item rod, Item catches, int biteDelay, int biteDelayWhenRaining, double maxAnglerDistance)
    {
        _rod = rod;
        _catch = catches;
        _biteDelay = biteDelay;
        _biteDelayWhenRaining = biteDelayWhenRaining;
        _maxAnglerDistanceSquared = maxAnglerDistance * maxAnglerDistance;

        _angler = layout.DeclareRef<EntityPlayer>();
        _hooked = layout.DeclareRef<Entity>();
        _inGround = layout.DeclareBool();
        _inTile = layout.DeclareInt();
        _shake = layout.DeclareInt();
        _ticksInAir = layout.DeclareInt();
        _ticksInGround = layout.DeclareInt();
        _ticksCatchable = layout.DeclareInt();
        _tileX = layout.DeclareInt(-1);
        _tileY = layout.DeclareInt(-1);
        _tileZ = layout.DeclareInt(-1);

        _syncTicks = layout.DeclareInt();
        _targetX = layout.DeclareDouble();
        _targetY = layout.DeclareDouble();
        _targetZ = layout.DeclareDouble();
        _targetYaw = layout.DeclareDouble();
        _targetPitch = layout.DeclareDouble();
        _syncedVelocityX = layout.DeclareDouble();
        _syncedVelocityY = layout.DeclareDouble();
        _syncedVelocityZ = layout.DeclareDouble();
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        nbt.SetShort("xTile", (short)self.State[_tileX]);
        nbt.SetShort("yTile", (short)self.State[_tileY]);
        nbt.SetShort("zTile", (short)self.State[_tileZ]);
        nbt.SetByte("inTile", (sbyte)self.State[_inTile]);
        nbt.SetByte("shake", (sbyte)self.State[_shake]);
        nbt.SetByte("inGround", (sbyte)(self.State[_inGround] ? 1 : 0));
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        self.State[_tileX] = nbt.GetShort("xTile");
        self.State[_tileY] = nbt.GetShort("yTile");
        self.State[_tileZ] = nbt.GetShort("zTile");
        self.State[_inTile] = nbt.GetByte("inTile") & 255;
        self.State[_shake] = nbt.GetByte("shake") & 255;
        self.State[_inGround] = nbt.GetByte("inGround") == 1;
    }

    /// <summary>A synced position becomes a target eased towards over the given ticks.</summary>
    public bool OnPositionSync(Entity self, double x, double y, double z, float yaw, float pitch, int steps)
    {
        self.State[_targetX] = x;
        self.State[_targetY] = y;
        self.State[_targetZ] = z;
        self.State[_targetYaw] = yaw;
        self.State[_targetPitch] = pitch;
        self.State[_syncTicks] = steps;
        self.VelocityX = self.State[_syncedVelocityX];
        self.VelocityY = self.State[_syncedVelocityY];
        self.VelocityZ = self.State[_syncedVelocityZ];
        return true;
    }

    /// <summary>The synced velocity is remembered, so the eased steps above can restore it each tick.</summary>
    public bool OnVelocityFromServer(Entity self, double vx, double vy, double vz)
    {
        self.State[_syncedVelocityX] = self.VelocityX = vx;
        self.State[_syncedVelocityY] = self.VelocityY = vy;
        self.State[_syncedVelocityZ] = self.VelocityZ = vz;
        return true;
    }

    public bool OnTickEntity(Entity self)
    {
        self.BaseTick();
        if (self.State[_syncTicks] > 0)
        {
            EaseTowardsSyncedPosition(self);
            return true;
        }

        if (!self.World.IsRemote)
        {
            EntityPlayer? angler = Angler(self);
            ItemStack? heldItem = angler?.GetHand();
            if (angler != null && (angler.Dead || !angler.IsAlive || heldItem == null || heldItem.GetItem() != _rod || self.GetSquaredDistance(angler) > _maxAnglerDistanceSquared))
            {
                self.MarkDead();
                angler.FishHook = null;
                return true;
            }

            if (self.State.GetRef(_hooked) is { } hooked)
            {
                if (!hooked.Dead)
                {
                    self.X = hooked.X;
                    self.Y = hooked.BoundingBox.MinY + hooked.Height * 0.8D;
                    self.Z = hooked.Z;
                    return true;
                }

                self.State.SetRef(_hooked, null!);
            }
        }

        if (self.State[_shake] > 0)
        {
            --self.State[_shake];
        }

        if (self.State[_inGround])
        {
            int blockId = self.World.Reader.GetBlockId(self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
            if (blockId == self.State[_inTile])
            {
                ++self.State[_ticksInGround];
                if (self.State[_ticksInGround] == 1200)
                {
                    self.MarkDead();
                }

                return true;
            }

            self.State[_inGround] = false;
            self.VelocityX *= self.Random.NextFloat() * 0.2F;
            self.VelocityY *= self.Random.NextFloat() * 0.2F;
            self.VelocityZ *= self.Random.NextFloat() * 0.2F;
            self.State[_ticksInGround] = 0;
            self.State[_ticksInAir] = 0;
        }
        else
        {
            ++self.State[_ticksInAir];
        }

        HookWhateverIsInTheWay(self);
        if (self.State[_inGround])
        {
            return true;
        }

        Drift(self);
        return true;
    }

    /// <summary>
    ///     Casts a bobber from the angler's hands and hangs it off them. A player has at most one,
    ///     which is what the rod checks to decide between casting and reeling.
    /// </summary>
    public static Entity Cast(IWorldContext world, EntityPlayer angler)
    {
        Entity bobber = EntityRegistry.ByName("fishhook").Create(world);
        FishingBobberBehavior hook = bobber.Behaviors.Find<FishingBobberBehavior>()!;
        bobber.State.SetRef(hook._angler, angler);
        angler.FishHook = bobber;
        bobber.SetPositionAndAnglesKeepPrevAngles(angler.X, angler.Y + 1.62D - angler.StandingEyeHeight, angler.Z, angler.Yaw, angler.Pitch);
        bobber.X -= MathHelper.Cos(bobber.Yaw / 180.0F * (float)Math.PI) * 0.16F;
        bobber.Y -= 0.1F;
        bobber.Z -= MathHelper.Sin(bobber.Yaw / 180.0F * (float)Math.PI) * 0.16F;
        bobber.SetPosition(bobber.X, bobber.Y, bobber.Z);
        const float speed = 0.4F;
        bobber.VelocityX = -MathHelper.Sin(bobber.Yaw / 180.0F * (float)Math.PI) * MathHelper.Cos(bobber.Pitch / 180.0F * (float)Math.PI) * speed;
        bobber.VelocityZ = MathHelper.Cos(bobber.Yaw / 180.0F * (float)Math.PI) * MathHelper.Cos(bobber.Pitch / 180.0F * (float)Math.PI) * speed;
        bobber.VelocityY = -MathHelper.Sin(bobber.Pitch / 180.0F * (float)Math.PI) * speed;
        hook.SetHeading(bobber, bobber.VelocityX, bobber.VelocityY, bobber.VelocityZ, 1.5F, 1.0F);
        return bobber;
    }

    /// <summary>Whose line this is, for the renderer that draws it back to their hands.</summary>
    public EntityPlayer? Angler(Entity self) => self.State.GetRef(_angler);

    /// <summary>What the bobber struck and is now riding, or null while it is still in flight.</summary>
    public Entity? Hooked(Entity self) => self.State.GetRef(_hooked);

    private void SetHeading(Entity self, double dirX, double dirY, double dirZ, float speed, float spread)
    {
        float length = MathHelper.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        dirX /= length;
        dirY /= length;
        dirZ /= length;
        dirX += self.Random.NextGaussian() * 0.0075F * spread;
        dirY += self.Random.NextGaussian() * 0.0075F * spread;
        dirZ += self.Random.NextGaussian() * 0.0075F * spread;
        dirX *= speed;
        dirY *= speed;
        dirZ *= speed;
        self.VelocityX = dirX;
        self.VelocityY = dirY;
        self.VelocityZ = dirZ;
        float horizontalLength = MathHelper.Sqrt(dirX * dirX + dirZ * dirZ);
        self.PrevYaw = self.Yaw = (float)(Math.Atan2(dirX, dirZ) * 180.0D / (float)Math.PI);
        self.PrevPitch = self.Pitch = (float)(Math.Atan2(dirY, horizontalLength) * 180.0D / (float)Math.PI);
        self.State[_ticksInGround] = 0;
    }

    private void EaseTowardsSyncedPosition(Entity self)
    {
        int steps = self.State[_syncTicks];
        double interpX = self.X + (self.State[_targetX] - self.X) / steps;
        double interpY = self.Y + (self.State[_targetY] - self.Y) / steps;
        double interpZ = self.Z + (self.State[_targetZ] - self.Z) / steps;

        double yawDelta = self.State[_targetYaw] - self.Yaw;
        while (yawDelta < -180.0D)
        {
            yawDelta += 360.0D;
        }

        while (yawDelta >= 180.0D)
        {
            yawDelta -= 360.0D;
        }

        self.Yaw = (float)(self.Yaw + yawDelta / steps);
        self.Pitch = (float)(self.Pitch + (self.State[_targetPitch] - self.Pitch) / steps);
        --self.State[_syncTicks];
        self.SetPosition(interpX, interpY, interpZ);
        self.SetRotation(self.Yaw, self.Pitch);
    }

    /// <summary>
    ///     Casts along this tick's movement: a struck entity that takes the hit becomes what the
    ///     bobber rides, and a struck block stops it dead.
    /// </summary>
    private void HookWhateverIsInTheWay(Entity self)
    {
        Vec3D rayStart = new(self.X, self.Y, self.Z);
        Vec3D rayEnd = new(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);
        HitResult hit = self.World.Reader.Raycast(rayStart, rayEnd);
        rayStart = new Vec3D(self.X, self.Y, self.Z);
        rayEnd = new Vec3D(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);
        if (hit.Type != HitResultType.Miss)
        {
            rayEnd = new Vec3D(hit.Pos.x, hit.Pos.y, hit.Pos.z);
        }

        EntityPlayer? angler = Angler(self);
        Entity? hitEntity = null;
        List<Entity> entities = self.World.Entities.GetEntities(self, self.BoundingBox.Stretch(self.VelocityX, self.VelocityY, self.VelocityZ).Expand(1.0D, 1.0D, 1.0D));
        double minHitDistance = 0.0D;

        foreach (Entity entity in entities)
        {
            if (!entity.HasCollision || (Equals(entity, angler) && self.State[_ticksInAir] < 5))
            {
                continue;
            }

            const float expandAmount = 0.3F;
            Box expandedBox = entity.BoundingBox.Expand(expandAmount, expandAmount, expandAmount);
            HitResult entityHit = expandedBox.Raycast(rayStart, rayEnd);
            if (entityHit.Type == HitResultType.Miss)
            {
                continue;
            }

            double distance = rayStart.distanceTo(entityHit.Pos);
            if (!(distance < minHitDistance) && minHitDistance != 0.0D)
            {
                continue;
            }

            hitEntity = entity;
            minHitDistance = distance;
        }

        if (hitEntity != null)
        {
            hit = new HitResult(hitEntity);
        }

        if (hit.Type == HitResultType.Miss)
        {
            return;
        }

        if (hit.Entity != null)
        {
            if (hit.Entity.Damage(angler, 0))
            {
                self.State.SetRef(_hooked, hit.Entity);
            }
        }
        else
        {
            self.State[_inGround] = true;
        }
    }

    /// <summary>Flight, float and bite: buoyancy from how much of the float is under water.</summary>
    private void Drift(Entity self)
    {
        self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
        float horizontalSpeed = MathHelper.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        self.Yaw = (float)(Math.Atan2(self.VelocityX, self.VelocityZ) * 180.0D / (float)Math.PI);
        self.Pitch = (float)(Math.Atan2(self.VelocityY, horizontalSpeed) * 180.0D / Math.PI);

        while (self.Pitch - self.PrevPitch < -180.0F)
        {
            self.PrevPitch -= 360.0F;
        }

        while (self.Pitch - self.PrevPitch >= 180.0F)
        {
            self.PrevPitch += 360.0F;
        }

        while (self.Yaw - self.PrevYaw < -180.0F)
        {
            self.PrevYaw -= 360.0F;
        }

        while (self.Yaw - self.PrevYaw >= 180.0F)
        {
            self.PrevYaw += 360.0F;
        }

        self.Pitch = self.PrevPitch + (self.Pitch - self.PrevPitch) * 0.2F;
        self.Yaw = self.PrevYaw + (self.Yaw - self.PrevYaw) * 0.2F;
        float drag = 0.92F;
        if (self.OnGround || self.HorizontalCollision)
        {
            drag = 0.5F;
        }

        const byte waterCheckSegments = 5;
        double waterSubmersion = 0.0D;

        for (int segment = 0; segment < waterCheckSegments; ++segment)
        {
            double segmentBottom = self.BoundingBox.MinY + (self.BoundingBox.MaxY - self.BoundingBox.MinY) * (segment + 0) / waterCheckSegments - 0.125D + 0.125D;
            double segmentTop = self.BoundingBox.MinY + (self.BoundingBox.MaxY - self.BoundingBox.MinY) * (segment + 1) / waterCheckSegments - 0.125D + 0.125D;
            Box segmentBox = new(self.BoundingBox.MinX, segmentBottom, self.BoundingBox.MinZ, self.BoundingBox.MaxX, segmentTop, self.BoundingBox.MaxZ);
            if (self.World.Reader.IsMaterialInBox(segmentBox, m => m == Material.Water))
            {
                waterSubmersion += 1.0D / waterCheckSegments;
            }
        }

        if (waterSubmersion > 0.0D)
        {
            TickBite(self);
        }

        if (self.State[_ticksCatchable] > 0)
        {
            self.VelocityY -= self.Random.NextFloat() * self.Random.NextFloat() * self.Random.NextFloat() * 0.2D;
        }

        double buoyancy = waterSubmersion * 2.0D - 1.0D;
        self.VelocityY += 0.04F * buoyancy;
        if (waterSubmersion > 0.0D)
        {
            drag = (float)(drag * 0.9D);
            self.VelocityY *= 0.8D;
        }

        self.VelocityX *= drag;
        self.VelocityY *= drag;
        self.VelocityZ *= drag;
        self.SetPosition(self.X, self.Y, self.Z);
    }

    /// <summary>Counts down a bite in progress, or rolls for a new one, sooner in the rain.</summary>
    private void TickBite(Entity self)
    {
        if (self.State[_ticksCatchable] > 0)
        {
            --self.State[_ticksCatchable];
            return;
        }

        int catchDelay = self.World.Environment.IsRainingAt(MathHelper.Floor(self.X), MathHelper.Floor(self.Y) + 1, MathHelper.Floor(self.Z))
            ? _biteDelayWhenRaining
            : _biteDelay;

        if (self.Random.NextInt(catchDelay) != 0)
        {
            return;
        }

        self.State[_ticksCatchable] = self.Random.NextInt(30) + 10;
        self.VelocityY -= 0.2F;
        self.World.Broadcaster.PlaySoundAtEntity(self, "random.splash", 0.25F, 1.0F + (self.Random.NextFloat() - self.Random.NextFloat()) * 0.4F);
        float waterSurface = MathHelper.Floor(self.BoundingBox.MinY);

        for (int particle = 0; particle < 1.0F + self.Width * 20.0F; ++particle)
        {
            float offsetX = (self.Random.NextFloat() * 2.0F - 1.0F) * self.Width;
            float offsetZ = (self.Random.NextFloat() * 2.0F - 1.0F) * self.Width;
            self.World.Broadcaster.AddParticle("bubble", self.X + offsetX, waterSurface + 1.0F, self.Z + offsetZ, self.VelocityX, self.VelocityY - self.Random.NextFloat() * 0.2F, self.VelocityZ);
        }

        for (int particle = 0; particle < 1.0F + self.Width * 20.0F; ++particle)
        {
            float offsetX = (self.Random.NextFloat() * 2.0F - 1.0F) * self.Width;
            float offsetZ = (self.Random.NextFloat() * 2.0F - 1.0F) * self.Width;
            self.World.Broadcaster.AddParticle("splash", self.X + offsetX, waterSurface + 1.0F, self.Z + offsetZ, self.VelocityX, self.VelocityY, self.VelocityZ);
        }
    }

    /// <summary>
    ///     Reels the line in and reports what the rod should wear for it: 3 for dragging a hooked
    ///     entity back, 2 for tearing it out of the ground, 1 for landing a fish, 0 for nothing.
    /// </summary>
    public int Reel(Entity self)
    {
        EntityPlayer? angler = Angler(self);
        byte wear = 0;

        if (self.State.GetRef(_hooked) is { } hooked)
        {
            if (angler != null)
            {
                YankTowardsAngler(self, angler, hooked, true);
            }

            wear = 3;
        }
        else if (self.State[_ticksCatchable] > 0)
        {
            Entity fish = DroppedItemBehavior.Create(self.World, self.X, self.Y, self.Z, new ItemStack(_catch));
            if (angler != null)
            {
                YankTowardsAngler(self, angler, fish, false);
            }

            self.World.SpawnEntity(fish);
            angler?.IncreaseStat(Stats.Stats.FishCaughtStat, 1);
            wear = 1;
        }

        if (self.State[_inGround])
        {
            wear = 2;
        }

        self.MarkDead();
        if (angler != null)
        {
            angler.FishHook = null;
        }

        return wear;
    }

    /// <summary>
    ///     Pulls towards the angler, plus a lob proportional to the square root of the distance.
    ///     A hooked entity keeps its own motion and has this added; a fresh fish is given it outright.
    /// </summary>
    private static void YankTowardsAngler(Entity self, EntityPlayer angler, Entity target, bool additive)
    {
        double deltaX = angler.X - self.X;
        double deltaY = angler.Y - self.Y;
        double deltaZ = angler.Z - self.Z;
        double distance = MathHelper.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);

        double pullX = deltaX * PullStrength;
        double pullY = deltaY * PullStrength + MathHelper.Sqrt(distance) * 0.08D;
        double pullZ = deltaZ * PullStrength;

        target.VelocityX = additive ? target.VelocityX + pullX : pullX;
        target.VelocityY = additive ? target.VelocityY + pullY : pullY;
        target.VelocityZ = additive ? target.VelocityZ + pullZ : pullZ;
    }
}
