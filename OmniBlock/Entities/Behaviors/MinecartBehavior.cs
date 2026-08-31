using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities.State;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A minecart in all three of its kinds: rideable, chest, and furnace. They share everything
///     substantial (following a rail, being flung along by powered track, trading momentum in a
///     pile-up, and breaking into the pieces that built them) and differ only by one stored number,
///     so they are one entity type rather than three.
///     <para>
///         That number also decides which spawn packet id the cart goes out on, so the wire ids are
///         declared as data here, in the same shape as falling sand's. See <c>wire_ids</c>.
///     </para>
/// </summary>
public sealed class MinecartBehavior : IEntityTicker, IEntityLifecycle, IEntityPersistence, IEntityInteractable, IEntityPhysics
{
    /// <summary>The rideable cart, which a wandering mob can be knocked into.</summary>
    public const int Rideable = 0;

    /// <summary>The chest cart, whose only interaction is opening it.</summary>
    public const int Chest = 1;

    /// <summary>The furnace cart, which burns coal to shove itself along.</summary>
    public const int Furnace = 2;

    private const double MaxSpeed = 0.4D;
    private const double SlopeAcceleration = 1.0D / 128.0D;
    private const double PoweredRailBoost = 0.06D;

    /// <summary>
    ///     The two ends each rail shape connects, as offsets from the rail block. Index is the rail's
    ///     metadata; the middle component is the vertical step a sloped end climbs.
    /// </summary>
    private static readonly int[][][] s_railShapeVectors =
    [
        [[0, 0, -1], [0, 0, 1]],
        [[-1, 0, 0], [1, 0, 0]],
        [[-1, -1, 0], [1, 0, 0]],
        [[-1, 0, 0], [1, -1, 0]],
        [[0, 0, -1], [0, -1, 1]],
        [[0, -1, -1], [0, 0, 1]],
        [[0, 0, 1], [1, 0, 0]],
        [[0, 0, 1], [-1, 0, 0]],
        [[0, 0, -1], [-1, 0, 0]],
        [[0, 0, -1], [1, 0, 0]]
    ];

    private readonly int _breakThreshold;
    private readonly StateHandle<MinecartCargo> _cargo;
    private readonly int _coalItemId;
    private readonly StateHandle<int> _damage;
    private readonly StateHandle<int> _fuel;
    private readonly int _fuelPerCoal;

    private readonly StateHandle<int> _lerpSteps;
    private readonly StateHandle<double> _pushX;
    private readonly StateHandle<double> _pushZ;

    private readonly StateHandle<int> _rockDirection;
    private readonly StateHandle<double> _syncedVelocityX;
    private readonly StateHandle<double> _syncedVelocityY;
    private readonly StateHandle<double> _syncedVelocityZ;
    private readonly StateHandle<double> _targetPitch;
    private readonly StateHandle<double> _targetX;
    private readonly StateHandle<double> _targetY;
    private readonly StateHandle<double> _targetYaw;
    private readonly StateHandle<double> _targetZ;
    private readonly StateHandle<int> _timeSinceHit;

    private readonly StateHandle<int> _type;

    /// <summary>Cart type to the object-spawn id it goes out on, and what each drops when broken.</summary>
    private readonly Dictionary<int, int> _wireIds;

    private readonly Dictionary<int, int[]> _wreckage;
    private readonly StateHandle<bool> _yawFlipped;

    public MinecartBehavior(
        EntityStateLayout layout,
        int breakDamage,
        int fuelItemId,
        int fuelPerCoal,
        Dictionary<int, int> wireIds,
        Dictionary<int, int[]> wreckage)
    {
        _breakThreshold = breakDamage;
        _fuelPerCoal = fuelPerCoal;
        _coalItemId = fuelItemId;
        _wireIds = wireIds;
        _wreckage = wreckage;

        _type = layout.DeclareInt();
        _cargo = layout.DeclareRef<MinecartCargo>();
        _fuel = layout.DeclareInt();
        _pushX = layout.DeclareDouble();
        _pushZ = layout.DeclareDouble();
        _yawFlipped = layout.DeclareBool();

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

    /// <summary>Riding, opening, or refuelling, depending on the cart's kind.</summary>
    public bool OnInteract(Entity self, EntityPlayer player)
    {
        switch (Type(self))
        {
            case Rideable:
                if (self.Passenger is EntityPlayer && !Equals(self.Passenger, player))
                {
                    return true;
                }

                if (!self.World.IsRemote)
                {
                    player.SetVehicle(self);
                }

                break;

            case Chest:
                if (!self.World.IsRemote && Cargo(self) is { } cargo)
                {
                    player.openChestScreen(cargo);
                }

                break;

            case Furnace:
                if (player.Inventory.ItemInHand is { } heldItem && heldItem.ItemId == _coalItemId)
                {
                    if (--heldItem.Count == 0)
                    {
                        player.Inventory.SetStack(player.Inventory.SelectedSlot, null);
                    }

                    self.State[_fuel] += _fuelPerCoal;
                }

                // The shove points away from whoever stoked it, which is how the cart is aimed.
                self.State[_pushX] = self.X - player.X;
                self.State[_pushZ] = self.Z - player.Z;
                break;
        }

        return true;
    }

    public bool? Damage(Entity self, Entity? attacker, int amount)
    {
        if (self.World.IsRemote || self.Dead)
        {
            return true;
        }

        self.State[_rockDirection] = -self.State[_rockDirection];
        self.State[_timeSinceHit] = 10;
        self.VelocityModified = true;
        self.State[_damage] += amount * 10;

        if (self.State[_damage] <= _breakThreshold)
        {
            return true;
        }

        self.Passenger?.SetVehicle(self);

        // MarkDead spills the cargo on the way out, so only the cart's own pieces are dropped here.
        self.MarkDead();
        foreach (int itemId in _wreckage.GetValueOrDefault(Type(self), []))
        {
            self.DropItem(itemId, 1, 0.0F);
        }

        return true;
    }

    public bool OnAnimateHurt(Entity self)
    {
        self.State[_rockDirection] = -self.State[_rockDirection];
        self.State[_timeSinceHit] = 10;

        // Verbatim from Beta: the animation packet carries no amount, and this value only feeds the
        // renderer's tip angle.
        self.State[_damage] += self.State[_damage] * 10;
        return true;
    }

    /// <summary>However a chest cart is removed, its contents end up on the ground.</summary>
    public void OnRemoved(Entity self)
    {
        if (Cargo(self) is not { } cargo)
        {
            return;
        }

        for (int slotIndex = 0; slotIndex < cargo.SlotCount; ++slotIndex)
        {
            if (cargo.GetStack(slotIndex) is not { } stack)
            {
                continue;
            }

            float offsetX = self.Random.NextFloat() * 0.8F + 0.1F;
            float offsetY = self.Random.NextFloat() * 0.8F + 0.1F;
            float offsetZ = self.Random.NextFloat() * 0.8F + 0.1F;

            while (stack.Count > 0)
            {
                int dropCount = Math.Min(self.Random.NextInt(21) + 10, stack.Count);
                stack.Count -= dropCount;

                Entity dropped = DroppedItemBehavior.Create(
                    self.World,
                    self.X + offsetX,
                    self.Y + offsetY,
                    self.Z + offsetZ,
                    new ItemStack(stack.ItemId, dropCount, stack.GetDamage()));

                const float scatterSpeed = 0.05F;
                dropped.VelocityX = (float)self.Random.NextGaussian() * scatterSpeed;
                dropped.VelocityY = (float)self.Random.NextGaussian() * scatterSpeed + 0.2F;
                dropped.VelocityZ = (float)self.Random.NextGaussian() * scatterSpeed;
                self.World.SpawnEntity(dropped);
            }
        }
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        int type = Type(self);
        nbt.SetInteger("Type", type);

        if (type == Furnace)
        {
            nbt.SetDouble("PushX", self.State[_pushX]);
            nbt.SetDouble("PushZ", self.State[_pushZ]);
            nbt.SetShort("Fuel", (short)self.State[_fuel]);
        }
        else if (type == Chest && Cargo(self) is { } cargo)
        {
            NBTTagList items = new();
            for (int slotIndex = 0; slotIndex < cargo.SlotCount; ++slotIndex)
            {
                if (cargo.GetStack(slotIndex) is not { } stack)
                {
                    continue;
                }

                NBTTagCompound itemTag = new();
                itemTag.SetByte("Slot", (sbyte)slotIndex);
                stack.WriteToNbt(itemTag);
                items.SetTag(itemTag);
            }

            nbt.SetTag("Items", items);
        }
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        int type = nbt.GetInteger("Type");
        self.State[_type] = type;

        if (type == Furnace)
        {
            self.State[_pushX] = nbt.GetDouble("PushX");
            self.State[_pushZ] = nbt.GetDouble("PushZ");
            self.State[_fuel] = nbt.GetShort("Fuel");
        }
        else if (type == Chest)
        {
            MinecartCargo cargo = new(self);
            self.State.SetRef(_cargo, cargo);

            NBTTagList items = nbt.GetTagList("Items");
            for (int i = 0; i < items.TagCount(); ++i)
            {
                NBTTagCompound itemTag = (NBTTagCompound)items.TagAt(i);
                int slotIndex = itemTag.GetByte("Slot") & 255;
                if (slotIndex >= 0 && slotIndex < cargo.SlotCount)
                {
                    cargo.SetStack(slotIndex, new ItemStack(itemTag));
                }
            }
        }
    }

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

    /// <summary>
    ///     Being bumped. A loose mob walked into by an empty rideable cart ends up riding it.
    ///     Otherwise the two share out their momentum, with a furnace cart shoving harder than it is
    ///     shoved.
    /// </summary>
    public bool OnCollision(Entity self, Entity other)
    {
        if (self.World.IsRemote || Equals(other, self.Passenger))
        {
            return true;
        }

        if (other is EntityLiving and not EntityPlayer &&
            Type(self) == Rideable &&
            self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ > 0.01D &&
            self.Passenger == null &&
            other.Vehicle == null)
        {
            other.SetVehicle(self);
        }

        double deltaX = other.X - self.X;
        double deltaZ = other.Z - self.Z;
        double distanceSq = deltaX * deltaX + deltaZ * deltaZ;
        if (distanceSq < 1.0E-4D)
        {
            return true;
        }

        double distance = MathHelper.Sqrt(distanceSq);
        deltaX /= distance;
        deltaZ /= distance;

        double forceScale = Math.Min(1.0D / distance, 1.0D);
        deltaX *= forceScale * 0.1F * 0.5D;
        deltaZ *= forceScale * 0.1F * 0.5D;

        if (other.Behaviors.Find<MinecartBehavior>() is not { } otherCart)
        {
            self.AddVelocity(-deltaX, 0.0D, -deltaZ);
            other.AddVelocity(deltaX / 4.0D, 0.0D, deltaZ / 4.0D);
            return true;
        }

        // Verbatim from Beta, including mixing the other cart's PrevX into what reads as an
        // alignment test: a glancing pile-up is ignored instead of resolved.
        double collisionAlignment = (other.X - self.X) * other.VelocityZ + (other.Z - self.Z) * other.PrevX;
        if (collisionAlignment * collisionAlignment > 5.0D)
        {
            return true;
        }

        double averageVelocityX = other.VelocityX + self.VelocityX;
        double averageVelocityZ = other.VelocityZ + self.VelocityZ;

        if (otherCart.Type(other) == Furnace && Type(self) != Furnace)
        {
            self.VelocityX *= 0.2F;
            self.VelocityZ *= 0.2F;
            self.AddVelocity(other.VelocityX - deltaX, 0.0D, other.VelocityZ - deltaZ);
            other.VelocityX *= 0.7F;
            other.VelocityZ *= 0.7F;
        }
        else if (otherCart.Type(other) != Furnace && Type(self) == Furnace)
        {
            other.VelocityX *= 0.2F;
            other.VelocityZ *= 0.2F;
            other.AddVelocity(self.VelocityX + deltaX, 0.0D, self.VelocityZ + deltaZ);
            self.VelocityX *= 0.7F;
            self.VelocityZ *= 0.7F;
        }
        else
        {
            averageVelocityX /= 2.0D;
            averageVelocityZ /= 2.0D;

            self.VelocityX *= 0.2F;
            self.VelocityZ *= 0.2F;
            self.AddVelocity(averageVelocityX - deltaX, 0.0D, averageVelocityZ - deltaZ);

            other.VelocityX *= 0.2F;
            other.VelocityZ *= 0.2F;
            other.AddVelocity(averageVelocityX + deltaX, 0.0D, averageVelocityZ + deltaZ);
        }

        return true;
    }

    public bool OnTickEntity(Entity self)
    {
        if (self.State[_timeSinceHit] > 0)
        {
            --self.State[_timeSinceHit];
        }

        if (self.State[_damage] > 0)
        {
            --self.State[_damage];
        }

        if (self.World.IsRemote)
        {
            TickClient(self);
            return true;
        }

        self.PrevX = self.X;
        self.PrevY = self.Y;
        self.PrevZ = self.Z;
        self.VelocityY -= 0.04D;

        int blockX = MathHelper.Floor(self.X);
        int blockY = MathHelper.Floor(self.Y);
        int blockZ = MathHelper.Floor(self.Z);

        if (IsRailBlock(self.World.Reader.GetBlockId(blockX, blockY - 1, blockZ)))
        {
            --blockY;
        }

        bool shouldEmitSmoke = false;
        int railBlockId = self.World.Reader.GetBlockId(blockX, blockY, blockZ);

        if (IsRailBlock(railBlockId))
        {
            shouldEmitSmoke = RideRail(self, blockX, blockY, blockZ, railBlockId);
        }
        else
        {
            RollFreely(self);
        }

        PointAlongTravel(self);
        BumpNeighbouringCarts(self);

        if (self.Passenger is { Dead: true })
        {
            self.Passenger = null;
        }

        if (shouldEmitSmoke && self.Random.NextInt(4) == 0)
        {
            BurnFuel(self);
        }

        return true;
    }

    /// <summary>
    ///     Places a cart of the given kind on the track. The <c>y</c> given is the rail height; the
    ///     cart body sits half its own height above it.
    /// </summary>
    public static Entity Place(IWorldContext world, double x, double y, double z, int type)
    {
        Entity cart = EntityRegistry.ByName("minecart").Create(world);
        MinecartBehavior rolling = cart.Behaviors.Find<MinecartBehavior>()!;
        cart.State[rolling._type] = type;
        if (type == Chest)
        {
            cart.State.SetRef(rolling._cargo, new MinecartCargo(cart));
        }

        rolling.SitOnTrack(cart, x, y, z);
        cart.VelocityX = cart.VelocityY = cart.VelocityZ = 0.0D;
        cart.PrevX = x;
        cart.PrevY = y;
        cart.PrevZ = z;
        return cart;
    }

    /// <summary>Whether this entity is a minecart.</summary>
    public static bool IsMinecart(Entity? entity) => entity?.Behaviors.Find<MinecartBehavior>() is not null;

    public int Type(Entity self) => self.State[_type];

    /// <summary>The chest this cart carries, or null for the two kinds that carry nothing.</summary>
    public MinecartCargo? Cargo(Entity self) => self.State.GetRef(_cargo);

    public int Fuel(Entity self) => self.State[_fuel];

    public int TimeSinceHit(Entity self) => self.State[_timeSinceHit];

    public int Damage(Entity self) => self.State[_damage];

    public int RockDirection(Entity self) => self.State[_rockDirection];

    /// <summary>
    ///     Which object-spawn id this cart goes out on. Three ids share one entity type, so the
    ///     tracker asks the behavior instead of reading the definition's single id.
    /// </summary>
    public int SpawnObjectId(Entity self) => _wireIds.GetValueOrDefault(Type(self));

    /// <summary>The cart type a given object-spawn id names, or null when it names none of them.</summary>
    public int? TypeForSpawnObjectId(int id)
    {
        foreach ((int type, int wireId) in _wireIds)
        {
            if (wireId == id)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>Rail height plus half the body, the convention every position update here uses.</summary>
    private void SitOnTrack(Entity self, double x, double trackY, double z) =>
        self.SetPosition(x, trackY + self.StandingEyeHeight, z);

    private void TickClient(Entity self)
    {
        if (self.State[_lerpSteps] > 0)
        {
            int steps = self.State[_lerpSteps];
            double interpolatedX = self.X + (self.State[_targetX] - self.X) / steps;
            double interpolatedY = self.Y + (self.State[_targetY] - self.Y) / steps;
            double interpolatedZ = self.Z + (self.State[_targetZ] - self.Z) / steps;

            double yawDelta = WrapDegrees(self.State[_targetYaw] - self.Yaw);
            self.Yaw = (float)(self.Yaw + yawDelta / steps);
            self.Pitch = (float)(self.Pitch + (self.State[_targetPitch] - self.Pitch) / steps);
            --self.State[_lerpSteps];

            self.SetPosition(interpolatedX, interpolatedY, interpolatedZ);
            self.SetRotation(self.Yaw, self.Pitch);
            return;
        }

        self.SetPosition(self.X, self.Y, self.Z);
        self.SetRotation(self.Yaw, self.Pitch);
    }

    /// <summary>
    ///     Rail-following for one tick: the slope pulls, the cart is snapped onto the rail's line and
    ///     moved along it, the ends are checked for a step up or down, drag is applied, and powered
    ///     rail either shoves it on or brings it to a stop. Returns whether the furnace should smoke.
    /// </summary>
    private bool RideRail(Entity self, int blockX, int blockY, int blockZ, int railBlockId)
    {
        bool shouldEmitSmoke = false;
        Vec3D? previousTrackPosition = GetTrackPosition(self, self.X, self.Y, self.Z);
        int railMeta = self.World.Reader.GetBlockMeta(blockX, blockY, blockZ);

        double trackY = blockY;
        bool poweredRailActive = false;
        bool poweredRailBraking = false;

        if (railBlockId == BlockRegistry.Get("powered_rail").Id)
        {
            poweredRailActive = (railMeta & 8) != 0;
            poweredRailBraking = !poweredRailActive;
        }

        if (RailBehavior.IsAlwaysStraight(BlockRegistry.GetByProtocolId(railBlockId)))
        {
            railMeta &= 7;
        }

        if (railMeta is >= 2 and <= 5)
        {
            trackY = blockY + 1;
        }

        switch (railMeta)
        {
            case 2: self.VelocityX -= SlopeAcceleration; break;
            case 3: self.VelocityX += SlopeAcceleration; break;
            case 4: self.VelocityZ += SlopeAcceleration; break;
            case 5: self.VelocityZ -= SlopeAcceleration; break;
        }

        int[][] railEnds = s_railShapeVectors[railMeta];
        double railDirX = railEnds[1][0] - railEnds[0][0];
        double railDirZ = railEnds[1][2] - railEnds[0][2];
        double railDirLength = Math.Sqrt(railDirX * railDirX + railDirZ * railDirZ);

        // Point the rail the way the cart is already going, so it keeps its heading through a turn.
        if (self.VelocityX * railDirX + self.VelocityZ * railDirZ < 0.0D)
        {
            railDirX = -railDirX;
            railDirZ = -railDirZ;
        }

        double horizontalSpeed = Math.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        self.VelocityX = horizontalSpeed * railDirX / railDirLength;
        self.VelocityZ = horizontalSpeed * railDirZ / railDirLength;

        if (poweredRailBraking)
        {
            Brake(self);
        }

        SnapOntoRailLine(self, blockX, blockZ, railEnds, trackY);

        double moveX = self.VelocityX;
        double moveZ = self.VelocityZ;

        if (self.Passenger != null)
        {
            moveX *= 0.75D;
            moveZ *= 0.75D;
        }

        self.Move(Math.Clamp(moveX, -MaxSpeed, MaxSpeed), 0.0D, Math.Clamp(moveZ, -MaxSpeed, MaxSpeed));

        // A sloped end lifts or drops the cart as it crosses into the next block.
        if (railEnds[0][1] != 0 && MathHelper.Floor(self.X) - blockX == railEnds[0][0] && MathHelper.Floor(self.Z) - blockZ == railEnds[0][2])
        {
            SitOnTrack(self, self.X, trackY + railEnds[0][1], self.Z);
        }
        else if (railEnds[1][1] != 0 && MathHelper.Floor(self.X) - blockX == railEnds[1][0] && MathHelper.Floor(self.Z) - blockZ == railEnds[1][2])
        {
            SitOnTrack(self, self.X, trackY + railEnds[1][1], self.Z);
        }

        if (self.Passenger != null)
        {
            self.VelocityX *= 0.997F;
            self.VelocityY = 0.0D;
            self.VelocityZ *= 0.997F;
        }
        else
        {
            if (Type(self) == Furnace)
            {
                shouldEmitSmoke = ApplyFurnacePush(self);
            }

            self.VelocityX *= 0.96F;
            self.VelocityY = 0.0D;
            self.VelocityZ *= 0.96F;
        }

        // Climbing costs speed and descending returns it, read off the height the cart actually moved.
        Vec3D? currentTrackPosition = GetTrackPosition(self, self.X, self.Y, self.Z);
        if (currentTrackPosition != null && previousTrackPosition != null)
        {
            double railHeightDeltaForce = (previousTrackPosition.Value.Y - currentTrackPosition.Value.Y) * 0.05D;
            horizontalSpeed = Math.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
            if (horizontalSpeed > 0.0D)
            {
                self.VelocityX = self.VelocityX / horizontalSpeed * (horizontalSpeed + railHeightDeltaForce);
                self.VelocityZ = self.VelocityZ / horizontalSpeed * (horizontalSpeed + railHeightDeltaForce);
            }

            SitOnTrack(self, self.X, currentTrackPosition.Value.Y, self.Z);
        }

        int currentBlockX = MathHelper.Floor(self.X);
        int currentBlockZ = MathHelper.Floor(self.Z);
        if (currentBlockX != blockX || currentBlockZ != blockZ)
        {
            horizontalSpeed = Math.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
            self.VelocityX = horizontalSpeed * (currentBlockX - blockX);
            self.VelocityZ = horizontalSpeed * (currentBlockZ - blockZ);
        }

        if (Type(self) == Furnace)
        {
            RealignFurnacePush(self);
        }

        if (poweredRailActive)
        {
            Boost(self, blockX, blockY, blockZ, railMeta);
        }

        return shouldEmitSmoke;
    }

    /// <summary>Unpowered powered rail is a brake: it halves speed, then stops the cart dead.</summary>
    private static void Brake(Entity self)
    {
        double brakingSpeed = Math.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        if (brakingSpeed < 0.03D)
        {
            self.VelocityX = self.VelocityY = self.VelocityZ = 0.0D;
            return;
        }

        self.VelocityX *= 0.5D;
        self.VelocityY = 0.0D;
        self.VelocityZ *= 0.5D;
    }

    /// <summary>
    ///     Powered rail pushes a moving cart harder along its own heading; a cart standing still on
    ///     one is nudged away from whichever side is walled in.
    /// </summary>
    private static void Boost(Entity self, int blockX, int blockY, int blockZ, int railMeta)
    {
        double speedMagnitude = Math.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        if (speedMagnitude > 0.01D)
        {
            self.VelocityX += self.VelocityX / speedMagnitude * PoweredRailBoost;
            self.VelocityZ += self.VelocityZ / speedMagnitude * PoweredRailBoost;
            return;
        }

        if (railMeta == 1)
        {
            if (self.World.Reader.ShouldSuffocate(blockX - 1, blockY, blockZ))
            {
                self.VelocityX = 0.02D;
            }
            else if (self.World.Reader.ShouldSuffocate(blockX + 1, blockY, blockZ))
            {
                self.VelocityX = -0.02D;
            }
        }
        else if (railMeta == 0)
        {
            if (self.World.Reader.ShouldSuffocate(blockX, blockY, blockZ - 1))
            {
                self.VelocityZ = 0.02D;
            }
            else if (self.World.Reader.ShouldSuffocate(blockX, blockY, blockZ + 1))
            {
                self.VelocityZ = -0.02D;
            }
        }
    }

    /// <summary>Places the cart exactly on the rail's centre line, keeping how far along it sits.</summary>
    private void SnapOntoRailLine(Entity self, int blockX, int blockZ, int[][] railEnds, double trackY)
    {
        double railStartX = blockX + 0.5D + railEnds[0][0] * 0.5D;
        double railStartZ = blockZ + 0.5D + railEnds[0][2] * 0.5D;
        double railDirX = blockX + 0.5D + railEnds[1][0] * 0.5D - railStartX;
        double railDirZ = blockZ + 0.5D + railEnds[1][2] * 0.5D - railStartZ;

        double positionAlongRail;
        if (railDirX == 0.0D)
        {
            self.X = blockX + 0.5D;
            positionAlongRail = self.Z - blockZ;
        }
        else if (railDirZ == 0.0D)
        {
            self.Z = blockZ + 0.5D;
            positionAlongRail = self.X - blockX;
        }
        else
        {
            positionAlongRail = ((self.X - railStartX) * railDirX + (self.Z - railStartZ) * railDirZ) * 2.0D;
        }

        self.X = railStartX + railDirX * positionAlongRail;
        self.Z = railStartZ + railDirZ * positionAlongRail;
        SitOnTrack(self, self.X, trackY, self.Z);
    }

    /// <summary>A stoked furnace cart drives itself along the direction it was last aimed.</summary>
    private bool ApplyFurnacePush(Entity self)
    {
        double pushMagnitude = MathHelper.Sqrt(self.State[_pushX] * self.State[_pushX] + self.State[_pushZ] * self.State[_pushZ]);
        if (pushMagnitude <= 0.01D)
        {
            self.VelocityX *= 0.9F;
            self.VelocityY = 0.0D;
            self.VelocityZ *= 0.9F;
            return false;
        }

        self.State[_pushX] /= pushMagnitude;
        self.State[_pushZ] /= pushMagnitude;

        const double furnaceAcceleration = 0.04D;
        self.VelocityX *= 0.8F;
        self.VelocityY = 0.0D;
        self.VelocityZ *= 0.8F;
        self.VelocityX += self.State[_pushX] * furnaceAcceleration;
        self.VelocityZ += self.State[_pushZ] * furnaceAcceleration;
        return true;
    }

    /// <summary>
    ///     Keeps the shove pointing the way the cart is actually rolling, and drops it entirely once
    ///     the cart has turned back on itself.
    /// </summary>
    private void RealignFurnacePush(Entity self)
    {
        double pushMagnitude = MathHelper.Sqrt(self.State[_pushX] * self.State[_pushX] + self.State[_pushZ] * self.State[_pushZ]);
        if (pushMagnitude <= 0.01D)
        {
            return;
        }

        if (self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ <= 0.001D)
        {
            return;
        }

        self.State[_pushX] /= pushMagnitude;
        self.State[_pushZ] /= pushMagnitude;

        if (self.State[_pushX] * self.VelocityX + self.State[_pushZ] * self.VelocityZ < 0.0D)
        {
            self.State[_pushX] = 0.0D;
            self.State[_pushZ] = 0.0D;
        }
        else
        {
            self.State[_pushX] = self.VelocityX;
            self.State[_pushZ] = self.VelocityZ;
        }
    }

    private void BurnFuel(Entity self)
    {
        if (--self.State[_fuel] < 0)
        {
            self.State[_pushX] = 0.0D;
            self.State[_pushZ] = 0.0D;
        }

        self.World.Broadcaster.AddParticle("largesmoke", self.X, self.Y + 0.8D, self.Z, 0.0D, 0.0D, 0.0D);
    }

    /// <summary>Off the rails the cart skids to a halt.</summary>
    private static void RollFreely(Entity self)
    {
        self.VelocityX = Math.Clamp(self.VelocityX, -MaxSpeed, MaxSpeed);
        self.VelocityZ = Math.Clamp(self.VelocityZ, -MaxSpeed, MaxSpeed);

        if (self.OnGround)
        {
            self.VelocityX *= 0.5D;
            self.VelocityY *= 0.5D;
            self.VelocityZ *= 0.5D;
        }

        self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);

        if (!self.OnGround)
        {
            self.VelocityX *= 0.95F;
            self.VelocityY *= 0.95F;
            self.VelocityZ *= 0.95F;
        }
    }

    /// <summary>
    ///     Faces the way it travelled. A cart that reverses keeps its facing and remembers that it is
    ///     running backwards, instead of spinning round.
    /// </summary>
    private void PointAlongTravel(Entity self)
    {
        self.Pitch = 0.0F;

        double deltaX = self.PrevX - self.X;
        double deltaZ = self.PrevZ - self.Z;
        if (deltaX * deltaX + deltaZ * deltaZ > 0.001D)
        {
            self.Yaw = (float)(Math.Atan2(deltaZ, deltaX) * 180.0D / Math.PI);
            if (self.State[_yawFlipped])
            {
                self.Yaw += 180.0F;
            }
        }

        double yawChange = WrapDegrees(self.Yaw - self.PrevYaw);
        if (yawChange < -170.0D || yawChange >= 170.0D)
        {
            self.Yaw += 180.0F;
            self.State[_yawFlipped] = !self.State[_yawFlipped];
        }

        self.SetRotation(self.Yaw, self.Pitch);
    }

    private static void BumpNeighbouringCarts(Entity self)
    {
        foreach (Entity other in self.World.Entities.GetEntities(self, self.BoundingBox.Expand(0.2D, 0.0D, 0.2D)))
        {
            if (!Equals(other, self.Passenger) && other.IsPushable && IsMinecart(other))
            {
                other.OnCollision(self);
            }
        }
    }

    /// <summary>
    ///     Where on the rail a point sits, a short distance further along the track. The renderer
    ///     samples ahead of and behind the cart to work out which way to tilt the model.
    /// </summary>
    public Vec3D? GetTrackPositionOffset(Entity self, double x, double y, double z, double distanceAlongTrack)
    {
        int blockX = MathHelper.Floor(x);
        int blockY = MathHelper.Floor(y);
        int blockZ = MathHelper.Floor(z);

        if (IsRailBlock(self.World.Reader.GetBlockId(blockX, blockY - 1, blockZ)))
        {
            --blockY;
        }

        int blockId = self.World.Reader.GetBlockId(blockX, blockY, blockZ);
        if (!IsRailBlock(blockId))
        {
            return null;
        }

        int railMeta = self.World.Reader.GetBlockMeta(blockX, blockY, blockZ);
        if (RailBehavior.IsAlwaysStraight(BlockRegistry.GetByProtocolId(blockId)))
        {
            railMeta &= 7;
        }

        y = railMeta is >= 2 and <= 5 ? blockY + 1 : blockY;

        int[][] railEnds = s_railShapeVectors[railMeta];
        double railDirX = railEnds[1][0] - railEnds[0][0];
        double railDirZ = railEnds[1][2] - railEnds[0][2];
        double railDirLength = Math.Sqrt(railDirX * railDirX + railDirZ * railDirZ);

        x += railDirX / railDirLength * distanceAlongTrack;
        z += railDirZ / railDirLength * distanceAlongTrack;

        if (railEnds[0][1] != 0 && MathHelper.Floor(x) - blockX == railEnds[0][0] && MathHelper.Floor(z) - blockZ == railEnds[0][2])
        {
            y += railEnds[0][1];
        }
        else if (railEnds[1][1] != 0 && MathHelper.Floor(x) - blockX == railEnds[1][0] && MathHelper.Floor(z) - blockZ == railEnds[1][2])
        {
            y += railEnds[1][1];
        }

        return GetTrackPosition(self, x, y, z);
    }

    /// <summary>Where on the rail's centre line a point sits, or null when it is not over rail.</summary>
    public Vec3D? GetTrackPosition(Entity self, double x, double y, double z)
    {
        int blockX = MathHelper.Floor(x);
        int blockY = MathHelper.Floor(y);
        int blockZ = MathHelper.Floor(z);

        if (IsRailBlock(self.World.Reader.GetBlockId(blockX, blockY - 1, blockZ)))
        {
            --blockY;
        }

        int blockId = self.World.Reader.GetBlockId(blockX, blockY, blockZ);
        if (!IsRailBlock(blockId))
        {
            return null;
        }

        int railMeta = self.World.Reader.GetBlockMeta(blockX, blockY, blockZ);
        if (RailBehavior.IsAlwaysStraight(BlockRegistry.GetByProtocolId(blockId)))
        {
            railMeta &= 7;
        }

        y = railMeta is >= 2 and <= 5 ? blockY + 1 : blockY;

        int[][] railEnds = s_railShapeVectors[railMeta];

        double railStartX = blockX + 0.5D + railEnds[0][0] * 0.5D;
        double railStartY = blockY + 0.5D + railEnds[0][1] * 0.5D;
        double railStartZ = blockZ + 0.5D + railEnds[0][2] * 0.5D;

        double railDirX = blockX + 0.5D + railEnds[1][0] * 0.5D - railStartX;
        double railDirY = (blockY + 0.5D + railEnds[1][1] * 0.5D - railStartY) * 2.0D;
        double railDirZ = blockZ + 0.5D + railEnds[1][2] * 0.5D - railStartZ;

        double positionAlongRail;
        if (railDirX == 0.0D)
        {
            x = blockX + 0.5D;
            positionAlongRail = z - blockZ;
        }
        else if (railDirZ == 0.0D)
        {
            z = blockZ + 0.5D;
            positionAlongRail = x - blockX;
        }
        else
        {
            positionAlongRail = ((x - railStartX) * railDirX + (z - railStartZ) * railDirZ) * 2.0D;
        }

        x = railStartX + railDirX * positionAlongRail;
        y = railStartY + railDirY * positionAlongRail;
        z = railStartZ + railDirZ * positionAlongRail;

        if (railDirY < 0.0D)
        {
            ++y;
        }

        if (railDirY > 0.0D)
        {
            y += 0.5D;
        }

        return new Vec3D(x, y, z);
    }

    private static bool IsRailBlock(int blockId) =>
        BlockRegistry.TryGetByProtocolId(blockId, out Block? block) && RailBehavior.IsRail(block);

    private static double WrapDegrees(double angle)
    {
        while (angle >= 180.0D)
        {
            angle -= 360.0D;
        }

        while (angle < -180.0D)
        {
            angle += 360.0D;
        }

        return angle;
    }
}
