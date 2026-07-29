using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Registries;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A boat: it floats on buoyancy sampled in five horizontal slices, is steered by the rider
///     leaning rather than by any control of its own, rocks when struck, and comes apart into its
///     planks and sticks when the damage builds up or it runs into a wall at speed.
///     <para>
///         Server and client tick differently — the server does the physics, the client eases
///         towards the positions it is sent — so the two halves are separate methods rather than one
///         body full of <c>IsRemote</c> checks.
///     </para>
/// </summary>
public sealed class BoatBehavior : IEntityTicker, IEntityLifecycle, IEntityPersistence, IEntityInteractable, IEntityPhysics
{
    private const double MaxHorizontalSpeed = 0.4D;
    private const double RiderInputAcceleration = 0.18D;
    private const double RiderTurnVelocityBlend = 0.25D;
    private const double YawSmoothing = 0.35D;

    /// <summary>Which way the hull tips when struck, flipped on every hit so knocks alternate.</summary>
    private readonly StateHandle<int> _rockDirection;
    private readonly StateHandle<int> _timeSinceHit;
    private readonly StateHandle<int> _damage;

    private readonly StateHandle<int> _lerpSteps;
    private readonly StateHandle<double> _targetX;
    private readonly StateHandle<double> _targetY;
    private readonly StateHandle<double> _targetZ;
    private readonly StateHandle<double> _targetYaw;
    private readonly StateHandle<double> _targetPitch;
    private readonly StateHandle<double> _syncedVelocityX;
    private readonly StateHandle<double> _syncedVelocityY;
    private readonly StateHandle<double> _syncedVelocityZ;

    private readonly int _breakThreshold;
    private readonly (int ItemId, int Count)[] _wreckage;

    public BoatBehavior(EntityStateLayout layout, int breakDamage, (int ItemId, int Count)[] wreckage)
    {
        _breakThreshold = breakDamage;
        _wreckage = wreckage;

        _rockDirection = layout.DeclareInt(1);
        _timeSinceHit = layout.DeclareInt();
        _damage = layout.DeclareInt();

        _lerpSteps = layout.DeclareInt();
        _targetX = layout.DeclareDouble();
        _targetY = layout.DeclareDouble();
        _targetZ = layout.DeclareDouble();
        _targetYaw = layout.DeclareDouble();
        _targetPitch = layout.DeclareDouble();
        _syncedVelocityX = layout.DeclareDouble();
        _syncedVelocityY = layout.DeclareDouble();
        _syncedVelocityZ = layout.DeclareDouble();
    }

    /// <summary>
    ///     Places a boat afloat. The <c>y</c> given is the waterline; the hull sits half its height
    ///     above it, which is why the caller's coordinate is not the entity's.
    /// </summary>
    public static Entity Launch(IWorldContext world, double x, double y, double z)
    {
        Entity boat = EntityRegistry.ByName("boat").Create(world);
        boat.SetPosition(x, y + boat.StandingEyeHeight, z);
        boat.VelocityX = boat.VelocityY = boat.VelocityZ = 0.0D;
        boat.PrevX = x;
        boat.PrevY = y;
        boat.PrevZ = z;
        return boat;
    }

    /// <summary>How far into its rocking the hull is, for the renderer that tips the model.</summary>
    public int TimeSinceHit(Entity self) => self.State[_timeSinceHit];

    public int Damage(Entity self) => self.State[_damage];

    public int RockDirection(Entity self) => self.State[_rockDirection];

    public bool? Damage(Entity self, Entity? attacker, int amount)
    {
        if (self.World.IsRemote || self.Dead) return true;

        Rock(self);
        self.State[_damage] += amount * 10;
        self.VelocityModified = true;

        if (self.State[_damage] <= _breakThreshold) return true;

        self.Passenger?.SetVehicle(self);
        BreakApart(self);

        return true;
    }

    /// <summary>The client's replay of a hit it was told about: the same rock, no damage tally.</summary>
    public bool OnAnimateHurt(Entity self)
    {
        Rock(self);

        // Verbatim from the beta class: doubling-plus-itself rather than adding the hit's amount,
        // which the animation packet does not carry. It only feeds the renderer's tip angle.
        self.State[_damage] += self.State[_damage] * 10;
        return true;
    }

    private void Rock(Entity self)
    {
        self.State[_rockDirection] = -self.State[_rockDirection];
        self.State[_timeSinceHit] = 10;
    }

    private void BreakApart(Entity self)
    {
        self.MarkDead();
        foreach ((int itemId, int count) in _wreckage)
        {
            for (int i = 0; i < count; ++i) self.DropItem(itemId, 1, 0.0F);
        }
    }

    /// <summary>A synced position becomes a target eased towards, plus two ticks of slack.</summary>
    public bool OnPositionSync(Entity self, double x, double y, double z, float yaw, float pitch, int steps)
    {
        self.State[_targetX] = x;
        self.State[_targetY] = y;
        self.State[_targetZ] = z;
        self.State[_targetYaw] = yaw;
        self.State[_targetPitch] = pitch;
        self.State[_lerpSteps] = steps + 2;
        self.VelocityX = self.State[_syncedVelocityX];
        self.VelocityY = self.State[_syncedVelocityY];
        self.VelocityZ = self.State[_syncedVelocityZ];
        return true;
    }

    public bool OnVelocityFromServer(Entity self, double vx, double vy, double vz)
    {
        self.State[_syncedVelocityX] = self.VelocityX = vx;
        self.State[_syncedVelocityY] = self.VelocityY = vy;
        self.State[_syncedVelocityZ] = self.VelocityZ = vz;
        return true;
    }

    /// <summary>The rider sits forward of the middle, along whichever way the hull points.</summary>
    public bool OnUpdatePassengerPosition(Entity self)
    {
        if (self.Passenger is not { } passenger) return true;

        double xOffset = Math.Cos(self.Yaw * Math.PI / 180.0D) * 0.4D;
        double zOffset = Math.Sin(self.Yaw * Math.PI / 180.0D) * 0.4D;
        passenger.SetPosition(self.X + xOffset, self.Y + PassengerRidingHeight(self) + passenger.StandingEyeHeight, self.Z + zOffset);
        return true;
    }

    private static double PassengerRidingHeight(Entity self) =>
        self.Type?.Definition is { } definition
            ? self.Height * definition.PassengerRideHeightScale + definition.PassengerRideOffset
            : 0.0D;

    /// <summary>Climbing in is the whole interaction; a boat someone else is in refuses.</summary>
    public bool OnInteract(Entity self, EntityPlayer player)
    {
        if (self.Passenger is EntityPlayer && !Equals(self.Passenger, player)) return true;
        if (!self.World.IsRemote) player.SetVehicle(self);
        return true;
    }

    public bool OnTickEntity(Entity self)
    {
        self.BaseTick();

        if (self.State[_timeSinceHit] > 0) --self.State[_timeSinceHit];
        if (self.State[_damage] > 0) --self.State[_damage];

        self.PrevX = self.X;
        self.PrevY = self.Y;
        self.PrevZ = self.Z;

        double waterSubmersion = MeasureSubmersion(self);

        if (self.World.IsRemote) TickClient(self);
        else TickServer(self, waterSubmersion);

        return true;
    }

    /// <summary>How much of the hull is under water, sampled in five horizontal slices.</summary>
    private static double MeasureSubmersion(Entity self)
    {
        const int waterSliceCount = 5;
        double submersion = 0.0D;

        for (int i = 0; i < waterSliceCount; ++i)
        {
            double sliceMinY = self.BoundingBox.MinY + (self.BoundingBox.MaxY - self.BoundingBox.MinY) * i / waterSliceCount - 0.125D;
            double sliceMaxY = self.BoundingBox.MinY + (self.BoundingBox.MaxY - self.BoundingBox.MinY) * (i + 1) / waterSliceCount - 0.125D;
            Box sliceBox = new(self.BoundingBox.MinX, sliceMinY, self.BoundingBox.MinZ, self.BoundingBox.MaxX, sliceMaxY, self.BoundingBox.MaxZ);

            if (self.World.Reader.IsMaterialInBox(sliceBox, m => m == Material.Water))
            {
                submersion += 1.0D / waterSliceCount;
            }
        }

        return submersion;
    }

    private void TickClient(Entity self)
    {
        if (self.State[_lerpSteps] > 0)
        {
            int steps = self.State[_lerpSteps];
            double nextX = self.X + (self.State[_targetX] - self.X) / steps;
            double nextY = self.Y + (self.State[_targetY] - self.Y) / steps;
            double nextZ = self.Z + (self.State[_targetZ] - self.Z) / steps;

            double yawDelta = WrapDegrees(self.State[_targetYaw] - self.Yaw);
            self.Yaw = (float)(self.Yaw + yawDelta / steps);
            self.Pitch = (float)(self.Pitch + (self.State[_targetPitch] - self.Pitch) / steps);

            --self.State[_lerpSteps];
            self.SetPosition(nextX, nextY, nextZ);
            self.SetRotation(self.Yaw, self.Pitch);
            return;
        }

        self.SetPosition(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);

        if (self.OnGround)
        {
            self.VelocityX *= 0.5D;
            self.VelocityY *= 0.5D;
            self.VelocityZ *= 0.5D;
        }

        self.VelocityX *= 0.99D;
        self.VelocityY *= 0.95D;
        self.VelocityZ *= 0.99D;

        self.Pitch = 0.0F;
        PointAlongTravel(self);
    }

    private void TickServer(Entity self, double waterSubmersion)
    {
        if (waterSubmersion < 1.0D)
        {
            double buoyancyFactor = waterSubmersion * 2.0D - 1.0D;
            self.VelocityY += 0.04D * buoyancyFactor;
        }
        else
        {
            if (self.VelocityY < 0.0D) self.VelocityY /= 2.0D;

            self.VelocityY += 0.007D;
        }

        ApplyRiderInput(self);

        self.VelocityX = Math.Clamp(self.VelocityX, -MaxHorizontalSpeed, MaxHorizontalSpeed);
        self.VelocityZ = Math.Clamp(self.VelocityZ, -MaxHorizontalSpeed, MaxHorizontalSpeed);

        if (self.OnGround)
        {
            self.VelocityX *= 0.5D;
            self.VelocityY *= 0.5D;
            self.VelocityZ *= 0.5D;
        }

        self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);

        double horizontalSpeed = Math.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        if (horizontalSpeed > 0.15D)
        {
            SpawnSplashParticles(self, horizontalSpeed);
        }

        // Running into a wall at speed is fatal: the hull comes apart where it struck.
        if (self.HorizontalCollision && horizontalSpeed > 0.15D)
        {
            if (!self.World.IsRemote) BreakApart(self);
        }
        else
        {
            self.VelocityX *= 0.99D;
            self.VelocityY *= 0.95D;
            self.VelocityZ *= 0.99D;
        }

        self.Pitch = 0.0F;
        PointAlongTravel(self);

        foreach (Entity entity in self.World.Entities.GetEntities(self, self.BoundingBox.Expand(0.2D, 0.0D, 0.2D)))
        {
            if (!Equals(entity, self.Passenger) && entity.IsPushable && entity.Behaviors.Find<BoatBehavior>() is not null)
            {
                entity.OnCollision(self);
            }
        }

        ClearSnowUnderfoot(self);

        if (self.Passenger is { Dead: true }) self.Passenger = null;
    }

    /// <summary>The rider steers by leaning: their own motion is what turns and drives the hull.</summary>
    private static void ApplyRiderInput(Entity self)
    {
        if (self.Passenger is not { } rider) return;

        self.VelocityX += rider.VelocityX * RiderInputAcceleration;
        self.VelocityZ += rider.VelocityZ * RiderInputAcceleration;

        double riderInputSpeedSq = rider.VelocityX * rider.VelocityX + rider.VelocityZ * rider.VelocityZ;
        if (riderInputSpeedSq <= 1.0E-4D) return;

        double speed = Math.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        if (speed <= 0.01D) return;

        double riderInputSpeed = Math.Sqrt(riderInputSpeedSq);
        double targetVelocityX = rider.VelocityX / riderInputSpeed * speed;
        double targetVelocityZ = rider.VelocityZ / riderInputSpeed * speed;

        self.VelocityX += (targetVelocityX - self.VelocityX) * RiderTurnVelocityBlend;
        self.VelocityZ += (targetVelocityZ - self.VelocityZ) * RiderTurnVelocityBlend;

        double desiredYaw = Math.Atan2(-targetVelocityZ, -targetVelocityX) * 180.0D / Math.PI;
        self.Yaw = (float)(self.Yaw + WrapDegrees(desiredYaw - self.Yaw) * YawSmoothing);
    }

    /// <summary>The hull swings to face the way it actually travelled, easing rather than snapping.</summary>
    private static void PointAlongTravel(Entity self)
    {
        double desiredYaw = self.Yaw;
        double motionX = self.PrevX - self.X;
        double motionZ = self.PrevZ - self.Z;

        if (motionX * motionX + motionZ * motionZ > 0.001D)
        {
            desiredYaw = Math.Atan2(motionZ, motionX) * 180.0D / Math.PI;
        }

        self.Yaw = (float)(self.Yaw + WrapDegrees(desiredYaw - self.Yaw) * YawSmoothing);
        self.SetRotation(self.Yaw, self.Pitch);
    }

    /// <summary>A boat ploughs a channel through snow rather than riding over it.</summary>
    private static void ClearSnowUnderfoot(Entity self)
    {
        int snowId = BlockRegistry.Get("snow").id;
        for (int i = 0; i < 4; ++i)
        {
            int snowX = MathHelper.Floor(self.X + (i % 2 - 0.5D) * 0.8D);
            int snowY = MathHelper.Floor(self.Y);
            int snowZ = MathHelper.Floor(self.Z + (i * 0.5F - 0.5D) * 0.8D);

            if (self.World.Reader.GetBlockId(snowX, snowY, snowZ) == snowId)
            {
                self.World.Writer.SetBlock(snowX, snowY, snowZ, 0);
            }
        }
    }

    private static void SpawnSplashParticles(Entity self, double horizontalSpeed)
    {
        double yawCos = Math.Cos(self.Yaw * Math.PI / 180.0D);
        double yawSin = Math.Sin(self.Yaw * Math.PI / 180.0D);

        for (int i = 0; i < 1.0D + horizontalSpeed * 60.0D; ++i)
        {
            double randomOffset = self.Random.NextFloat() * 2.0F - 1.0F;
            double sideOffset = (self.Random.NextInt(2) * 2 - 1) * 0.7D;

            double particleX;
            double particleZ;

            if (self.Random.NextBoolean())
            {
                particleX = self.X - yawCos * randomOffset * 0.8D + yawSin * sideOffset;
                particleZ = self.Z - yawSin * randomOffset * 0.8D - yawCos * sideOffset;
            }
            else
            {
                particleX = self.X + yawCos + yawSin * randomOffset * 0.7D;
                particleZ = self.Z + yawSin - yawCos * randomOffset * 0.7D;
            }

            self.World.Broadcaster.AddParticle("splash", particleX, self.Y - 0.125D, particleZ, self.VelocityX, self.VelocityY, self.VelocityZ);
        }
    }

    private static double WrapDegrees(double angle)
    {
        while (angle >= 180.0D) angle -= 360.0D;
        while (angle < -180.0D) angle += 360.0D;
        return angle;
    }

    /// <summary>A boat carries nothing across a save: where it floats is all there is to it.</summary>
    public void OnWriteNbt(Entity self, NBTTagCompound nbt) { }

    public void OnReadNbt(Entity self, NBTTagCompound nbt) { }
}
