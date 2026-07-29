using BetaSharp.Blocks.Materials;
using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.Util;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core.Systems;
using DroppedItemBehavior = BetaSharp.Entities.Behaviors.DroppedItemBehavior;

namespace BetaSharp.Entities;

public abstract partial class Entity : IEntity
{
    private static int s_nextEntityId;
    private readonly SyncedProperty<byte> _flags;
    private readonly EntityType? _type;
    private bool _firstTick = true;
    public Box BoundingBox = new(0.0D, 0.0D, 0.0D, 0.0D, 0.0D, 0.0D);

    protected Entity(IWorldContext world, EntityType? type = null)
    {
        World = world;
        SetPosition(0.0D, 0.0D, 0.0D);
        _flags = DataSynchronizer.MakeProperty<byte>(0, 0);

        // Prefer the type handed down by the registry factory: two registered types may share one
        // class, so the class alone does not identify the entity. The lookup is the fallback for
        // entities constructed directly (tests, the client's player subclasses).
        type ??= EntityRegistry.ByRuntimeType(GetType());
        _type = type;
        EntityBehaviorSet behaviors = type?.Behaviors ?? EntityBehaviorSet.Empty;
        Behaviors = behaviors;
        State = behaviors.StateLayout.Create();

        // Declared here, not on EntityLiving, so every entity kind can carry synced state.
        SyncedDeclarations = type?.Definition?.SyncedProperties ?? [];
        SyncedPropertyFactory.Declare(DataSynchronizer, SyncedDeclarations, type?.Id ?? GetType().Name);
    }

    /// <summary>JSON-declared synced properties for this entity's type.</summary>
    protected internal SyncedPropertyDefinition[] SyncedDeclarations { get; }

    /// <summary>Composed NBT persistence, for state a declared property cannot express on its own.</summary>
    protected internal IEntityPersistence? Persistence => Behaviors.Persistence;

    /// <summary>Composed player interaction, or <c>null</c> for entities that ignore the player.</summary>
    protected internal IEntityInteractable? Interactable => Behaviors.Interactable;

    /// <summary>Composed movement and collision response, or <c>null</c> for the default physics.</summary>
    protected internal IEntityPhysics? Physics => Behaviors.Physics;

    /// <summary>
    ///     Shared capability slots for this entity's type. Public because the client reads them:
    ///     a renderer asks the entity what it is composed of, not what class it is.
    /// </summary>
    public EntityBehaviorSet Behaviors { get; }

    /// <summary>
    ///     Per-entity storage for the slots this type's behaviors declared. Behaviors are shared, so
    ///     everything mutable lives here.
    /// </summary>
    public EntityState State { get; }

    /// <summary>Composed per-tick behavior, or <c>null</c> for entities that declare none.</summary>
    protected internal IEntityTicker? Ticker => Behaviors.Ticker;

    /// <summary>
    ///     The registered type this entity was created as, carried from construction. Falls back to a
    ///     lookup by runtime class for entities built outside the registry, which walks base classes
    ///     so client-side player subclasses still resolve to the registered <c>player</c> type.
    /// </summary>
    public virtual EntityType? Type => _type;

    public int ID { get; set; } = s_nextEntityId++;

    /// <summary>
    ///     Multiplayer for rendering, based of the render distance,
    /// </summary>
    protected double RenderDistanceWeight { get; init; } = 1.0D;

    /// <summary>
    ///     Prevents another entity spawning near/in this entity.
    /// </summary>
    public bool PreventEntitySpawning { get; protected init; }

    public Entity? Passenger { get; set; }
    public Entity? Vehicle { get; set; }
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
    public bool OnGround { get; set; }

    /// <summary>
    ///     If a collision occured in the X or Z directions.
    /// </summary>
    protected internal bool HorizontalCollision { get; private set; }

    /// <summary>
    ///     If a collision occured in the Y direction.
    /// </summary>
    private bool VerticalCollision { get; set; }

    /// <summary>
    ///     If a collision occured in either the X, Y, OR Z directions.
    /// </summary>
    public bool HasCollided { get; set; }

    public bool VelocityModified { get; set; }
    public bool Slowed { get; set; }
    public bool Dead { get; set; }
    public float Width { get; private set; } = 0.6F;
    public float Height { get; private set; } = 1.8F;
    public float PrevHorizontalSpeed { get; private set; }
    public float HorizontalSpeed { get; private set; }
    protected float FallDistance { get; set; }
    public double LastTickX { get; set; }
    public double LastTickY { get; set; }
    public double LastTickZ { get; set; }
    public float CameraOffset { get; set; }
    protected float StepHeight { get; init; }
    protected bool NoClip { get; init; }
    protected static float PushSpeedReduction => 0.0F;
    public JavaRandom Random { get; } = new();
    public int Age { get; private set; }
    protected int FireImmunityTicks { get; init; } = 1;
    protected internal int FireTicks { get; set; }
    public static int MaxAir => 300;
    protected internal bool InWater { get; private set; }
    public int Hearts { get; protected set; }
    public int Air { get; protected set; } = 300;
    public string? CloakUrl { get; set; }
    protected internal bool IsImmuneToFire { get; set; }
    public DataSynchronizer DataSynchronizer { get; } = new();
    public float MinBrightness { get; set; }
    public bool IsPersistent { get; set; }
    public int ChunkX { get; set; }
    public int ChunkSlice { get; set; }
    public int ChunkZ { get; set; }
    public int TrackedPosX { get; set; }
    public int TrackedPosY { get; set; }
    public int TrackedPosZ { get; set; }

    /// <summary>
    ///     If an entity should render even IF it's outside the viewing angle.
    /// </summary>
    public bool IgnoreFrustumCheck { get; init; }

    public float StandingEyeHeight { get; protected internal set; }

    protected virtual double PassengerRidingHeight => Height * 0.75D;

    public virtual float TargetingMargin => 0.1F;

    public virtual Vec3D? LookVector => null;

    public bool IsOnFire => FireTicks > 0 || GetFlag(0);

    public bool HasVehicle => Vehicle != null || GetFlag(2);

    public virtual ItemStack?[] Equipment => null;

    protected internal bool IsWet => InWater || World.Environment.IsRainingAt(MathHelper.Floor(X), MathHelper.Floor(Y), MathHelper.Floor(Z));

    /// <summary>
    ///     Whether the entity counts as in water. Readers that must not disturb the entity ask
    ///     <see cref="InWater" /> directly instead, because the Physics slot's answer can move it.
    /// </summary>
    protected internal bool IsInWater => Behaviors.Physics?.IsInWater(this) ?? InWater;

    protected internal bool IsTouchingLava => World.Reader.IsMaterialInBox(BoundingBox.Expand(-0.1F, -0.4F, -0.1F), m => m == Material.Lava);

    public virtual bool IsAlive => !Dead;

    public virtual bool CanBeTargeted => IsAlive;

    public virtual float EyeHeight => 0.0F;

    public virtual bool HasCollision => false;

    public virtual bool IsPushable => false;
    public int GetId() => ID;
    public IWorldContext World { get; private set; }

    public Vec3D Position => new(X, Y, Z);

    public virtual void Tick()
    {
        if (Ticker?.OnTickEntity(this) == true)
        {
            return;
        }

        Ticker?.OnTick(this);
        BaseTick();
    }

    /// <summary>
    ///     Reads a declared synced property by name, or <c>null</c> if this entity's type declares
    ///     none by that name. Callers ask what an entity <em>has</em>, not what class it is: saddling
    ///     works on anything declaring <c>saddled</c>, not specifically on a pig.
    /// </summary>
    public SyncedProperty<T>? Synced<T>(string name)
    {
        foreach (SyncedPropertyDefinition declaration in SyncedDeclarations)
        {
            if (declaration.Name == name)
            {
                return DataSynchronizer.Get<T>(declaration.Id);
            }
        }

        return null;
    }

    public virtual void MarkDead() => Dead = true;

    public virtual void BaseTick()
    {
        if (Vehicle is { Dead: true })
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
                float volume = MathHelper.Sqrt(VelocityX * VelocityX * 0.2F + VelocityY * VelocityY + VelocityZ * VelocityZ * 0.2F) * 0.2F;
                if (volume > 1.0F)
                {
                    volume = 1.0F;
                }

                World.Broadcaster.PlaySoundAtEntity(this, "random.splash", volume, 1.0F + (Random.NextFloat() - Random.NextFloat()) * 0.4F);
                float floorMinY = MathHelper.Floor(BoundingBox.MinY);

                for (int i = 0; i < 1.0F + Width * 20.0F; ++i)
                {
                    double xOffset = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    double zOffset = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    World.Broadcaster.AddParticle("bubble", X + xOffset, floorMinY + 1.0D, Z + zOffset, VelocityX, VelocityY - Random.NextFloat() * 0.2D, VelocityZ);

                    xOffset = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    zOffset = (Random.NextFloat() * 2.0F - 1.0F) * Width;
                    World.Broadcaster.AddParticle("splash", X + xOffset, floorMinY + 1.0D, Z + zOffset, VelocityX, VelocityY, VelocityZ);
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
                    Damage(null, 1);
                }

                --FireTicks;
            }
        }

        if (IsTouchingLava)
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

    private void SetOnFire()
    {
        if (IsImmuneToFire)
        {
            return;
        }

        Damage(null, 4);
        FireTicks = 600;
    }

    protected virtual void TickInVoid() => MarkDead();

    protected virtual void Damage(int amt)
    {
        if (!IsImmuneToFire)
        {
            Damage(null, amt);
        }
    }

    public float GetBrightnessAtEyes(float tickDelta)
    {
        int floorX = MathHelper.Floor(X);
        double eyeOffset = (BoundingBox.MaxY - BoundingBox.MinY) * 0.66D;
        int floorY = MathHelper.Floor(Y - StandingEyeHeight + eyeOffset);
        int floorZ = MathHelper.Floor(Z);

        int minX = MathHelper.Floor(BoundingBox.MinX);
        int minY = MathHelper.Floor(BoundingBox.MinY);
        int minZ = MathHelper.Floor(BoundingBox.MinZ);
        int maxX = MathHelper.Floor(BoundingBox.MaxX);
        int maxY = MathHelper.Floor(BoundingBox.MaxY);
        int maxZ = MathHelper.Floor(BoundingBox.MaxZ);

        int h = ChuckFormat.WorldHeight - 1;
        minY = Math.Clamp(minY, 0, h);
        maxY = Math.Clamp(maxY, 0, h);

        if (!World.ChunkHost.IsRegionLoaded(minX, minY, minZ, maxX, maxY, maxZ))
        {
            return MinBrightness;
        }

        float lum = World.Lighting.GetLuminance(floorX, floorY, floorZ);
        if (lum < MinBrightness)
        {
            lum = MinBrightness;
        }

        return lum;
    }

    public virtual void SetWorld(IWorldContext world) => World = world;

    public virtual void OnPlayerInteraction(EntityPlayer player) => Interactable?.OnPlayerCollision(this, player);

    public virtual bool Damage(Entity? entity, int amount)
    {
        ScheduleVelocityUpdate();
        return false;
    }

    public virtual void UpdateKilledAchievement(Entity entity, int score)
    {
    }

    public virtual bool ShouldRender(Vec3D vec) => ShouldRender(GetSquaredDistance(vec.x, vec.y, vec.z));

    protected virtual bool ShouldRender(double sqDist)
    {
        double edgeLength = BoundingBox.AverageEdgeLength;
        edgeLength *= 64.0D * RenderDistanceWeight;
        return sqDist < edgeLength * edgeLength;
    }

    public virtual float GetShadowRadius() => Height / 2.0F;

    protected internal void DropItem(int id, int count) => DropItem(id, count, 0.0F);

    protected internal Entity DropItem(int id, int count, float y) => DropItem(new ItemStack(id, count, 0), y);

    protected internal Entity DropItem(ItemStack stack, float y)
    {
        Entity item = DroppedItemBehavior.Create(World, X, Y + y, Z, stack, 10);
        World.SpawnEntity(item);
        return item;
    }

    public virtual bool Interact(EntityPlayer player) => Interactable?.OnInteract(this, player) ?? false;

    public virtual void TickPortalCooldown()
    {
    }

    public virtual void ProcessServerEntityStatus(sbyte statusId)
    {
    }

    public virtual void AnimateHurt()
    {
    }

    public virtual void UpdateCloak()
    {
    }

    public virtual void SetEquipmentStack(int slotIndex, int itemId, int damage)
    {
    }

    public virtual bool IsSneaking() => GetFlag(1);

    public void SetSneaking(bool sneaking) => SetFlag(1, sneaking);

    private bool GetFlag(int index) => (_flags.Value & (1 << index)) != 0;

    private void SetFlag(int index, bool value)
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

    public virtual void OnStruckByLightning(Entity bolt)
    {
        Damage(5);
        ++FireTicks;
        if (FireTicks == 0)
        {
            FireTicks = 300;
        }
    }

    public virtual void OnKillOther(EntityLiving entityLiving)
    {
    }

    public override bool Equals(object? other) => other is Entity e && e.ID == ID;

    public override int GetHashCode() => ID;
}
