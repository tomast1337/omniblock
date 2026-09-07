using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Network.Messages;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities;

/// <summary>
///     A mob. Concrete, not abstract: a mob with no pathfinding and no melee is nothing but this
///     plus its behaviors. A ghast hovers, aims and fires entirely from its Ticker and Physics slots.
/// </summary>
public class EntityLiving : Entity
{
    public EntityLiving(IWorldContext world, EntityType type) : base(world, type)
    {
        var definition = type.Definition;
        Definition = definition ?? EntityDefinition.Default;
        PreventEntitySpawning = true;
        SetPosition(X, Y, Z);
        Yaw = System.Random.Shared.NextSingle() * (float)Math.PI * 2.0f;
        StepHeight = 0.5F;

        if (definition is null)
        {
            return;
        }

        Health = definition.Health;
        MovementSpeed = definition.MovementSpeed;
        Texture = definition.Texture;
        IsImmuneToFire = definition.FireImmune;
        SetBoundingBoxSpacing(definition.Width, definition.Height);

        // Applied on top of the unscaled box, so the giant's box keeps the float artifacts of
        // multiplying its own Width and Height.
        if (definition.Scale != 1.0F)
        {
            StandingEyeHeight *= definition.Scale;
            SetBoundingBoxSpacing(Width * definition.Scale, Height * definition.Scale);
        }

        if (definition.HeldItem is { } held)
        {
            HeldItem = new ItemStack(world.Content.Items.Get(ResourceLocation.Parse(held)), 1);
        }
    }

    /// <summary>
    ///     This mob's configuration. The properties below read from it, so only genuinely dynamic
    ///     values need a behavior (a wolf's mood-dependent bark, its taming-dependent despawn).
    /// </summary>
    protected internal EntityDefinition Definition { get; }

    protected static int MaxHealth => 20;
    public float BodyYaw { get; set; }
    public float LastBodyYaw { get; private set; }
    protected float LastWalkProgress { get; set; }
    private float WalkProgress { get; set; }
    private float TotalWalkDistance { get; set; }
    protected float LastTotalWalkDistance { get; set; }
    protected bool CanLookAround { get; set; } = true;
    protected internal string Texture { get; set; } = "/mob/char.png";
    protected float RotationOffset { get; set; } = 0.0F;
    protected string? ModelName { get; set; } = null;
    protected float ModelScale { get; set; } = 1.0F;
    private int ScoreAmount { get; } = 0;
    public bool InterpolateOnly { get; set; } = false;
    private float LastSwingAnimationProgress { get; set; }
    protected float SwingAnimationProgress { get; set; }
    public int Health { get; protected internal set; } = 10;
    public int LastHealth { get; protected set; }
    private int LivingSoundTime { get; set; }
    public int HurtTime { get; private set; }
    public int MaxHurtTime { get; private set; }
    public float AttackedAtYaw { get; private set; }
    public int DeathTime { get; protected set; }
    protected internal int AttackTime { get; set; }

    /// <summary>Composed death-drop behavior. <c>null</c> means the mob drops nothing.</summary>
    public IEntityLootBehavior? Loot => Behaviors.Loot;

    /// <summary>Composed single-shot event reactions (death split, lightning conversion).</summary>
    public IEntityLifecycle? Lifecycle => Behaviors.Lifecycle;

    public float CameraPitch { get; private set; }
    public float Tilt { get; protected set; }
    public float LastWalkAnimationSpeed { get; protected internal set; }
    public float WalkAnimationSpeed { get; protected internal set; }
    public float AnimationPhase { get; protected internal set; }
    private int NewPosRotationIncrements { get; set; }
    private double NewPosX { get; set; }
    private double NewPosY { get; set; }
    private double NewPosZ { get; set; }
    private double NewRotationYaw { get; set; }
    private double NewRotationPitch { get; set; }
    protected int DamageForDisplay { get; set; }
    protected internal int EntityAge { get; set; }
    protected internal float SidewaysSpeed { get; set; }
    protected internal float ForwardSpeed { get; set; }
    private float RotationSpeed { get; set; }
    protected internal bool Jumping { get; set; }
    private static float DefaultPitch => 0.0F;
    protected internal float MovementSpeed { get; set; } = 0.7F;
    protected int LookTimer { get; set; }

    public override Vec3D? LookVector => GetLook(1.0F);

    public override bool IsAlive => !Dead && Health > 0;

    public override bool HasCollision => !Dead;

    public override bool IsPushable => !Dead;

    public override float EyeHeight => Height * Definition.EyeHeightScale;

    protected internal virtual float SoundVolume => Definition.SoundVolume;

    protected virtual string? LivingSound => Ticker?.LivingSound(this) ?? Definition.LivingSound;

    protected virtual string? HurtSound => Definition.HurtSound;

    protected virtual string? DeathSound => Definition.DeathSound;

    protected virtual bool IsOnLadder
    {
        get
        {
            if (Physics?.IsClimbing(this) is { } climbing)
            {
                return climbing;
            }

            var x = MathHelper.Floor(X);
            var y = MathHelper.Floor(BoundingBox.MinY);
            var z = MathHelper.Floor(Z);
            return World.Reader.GetBlockId(x, y, z) == World.Content.Blocks.Get("omniblock:ladder").Id;
        }
    }

    protected override double PassengerRidingHeight => base.PassengerRidingHeight + Definition.PassengerRideOffset;

    protected internal bool HasCurrentTarget => CurrentTarget != null;

    protected internal Entity? CurrentTarget { get; private set; }

    public virtual bool IsSleeping => false;

    /// <summary>Declared in the mob's JSON and resolved once at construction, not per render frame.</summary>
    public virtual ItemStack? HeldItem { get; }

    protected virtual int TalkInterval => Definition.TalkInterval;

    protected virtual float AirSpeed => 0.02f;

    protected virtual bool CanDespawn => Persistence?.CanDespawn(this) ?? Definition.CanDespawn;

    public virtual int MaxSpawnedInChunk => Definition.MaxSpawnedInChunk;

    /// <summary>Keeps the mob watching whatever it is looking at for another few ticks.</summary>
    protected internal void HoldGaze(int ticks) => LookTimer = ticks;

    protected override bool BypassesSteppingEffects() => Definition.MakesStepSounds;

    public virtual void PostSpawn() => Behaviors.Lifecycle?.OnPostSpawn(this);


    public bool CanSee(Entity entity) => World.Reader.Raycast(new Vec3D(X, Y + EyeHeight, Z), new Vec3D(entity.X, entity.Y + entity.EyeHeight, entity.Z)).Type == HitResultType.Miss;

    public virtual string GetTexture() => Texture;

    public void PlayLivingSound()
    {
        var sound = LivingSound;
        if (sound != null)
        {
            World.Broadcaster.PlaySoundAtEntity(this, sound, SoundVolume, (Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F);
        }
    }

    public override void BaseTick()
    {
        LastSwingAnimationProgress = SwingAnimationProgress;
        base.BaseTick();
        if (Random.NextInt(1000) < LivingSoundTime++)
        {
            LivingSoundTime = -TalkInterval;
            PlayLivingSound();
        }

        if (IsAlive && IsInsideWall())
        {
            Damage(null, 1);
        }

        if (IsImmuneToFire || World.IsRemote)
        {
            FireTicks = 0;
        }

        if (IsAlive && IsInFluid(Material.Water) && !canBreatheUnderwater())
        {
            --Air;
            if (Air == -20)
            {
                Air = 0;

                for (var i = 0; i < 8; ++i)
                {
                    var offsetX = Random.NextFloat() - Random.NextFloat();
                    var offsetY = Random.NextFloat() - Random.NextFloat();
                    var offsetZ = Random.NextFloat() - Random.NextFloat();
                    World.Broadcaster.AddParticle("bubble", X + offsetX, Y + offsetY, Z + offsetZ, VelocityX, VelocityY, VelocityZ);
                }

                Damage(null, 2);
            }

            FireTicks = 0;
        }
        else
        {
            Air = MaxAir;
        }

        CameraPitch = Tilt;
        if (AttackTime > 0)
        {
            --AttackTime;
        }

        if (HurtTime > 0)
        {
            --HurtTime;
        }

        if (Hearts > 0)
        {
            --Hearts;
        }

        if (Health <= 0)
        {
            ++DeathTime;
            if (DeathTime > 20)
            {
                OnEntityDeath();
                MarkDead();

                for (var i = 0; i < 20; ++i)
                {
                    var velX = Random.NextGaussian() * 0.02D;
                    var velY = Random.NextGaussian() * 0.02D;
                    var velZ = Random.NextGaussian() * 0.02D;
                    World.Broadcaster.AddParticle("explode", X + Random.NextFloat() * Width * 2.0F - Width, Y + Random.NextFloat() * Height, Z + Random.NextFloat() * Width * 2.0F - Width, velX, velY, velZ);
                }
            }
        }

        LastTotalWalkDistance = TotalWalkDistance;
        LastBodyYaw = BodyYaw;
        PrevYaw = Yaw;
        PrevPitch = Pitch;
    }

    public override void Move(double x, double y, double z)
    {
        if (!InterpolateOnly /* || this is ClientPlayerEntity*/)
        {
            base.Move(x, y, z);
        }
    }

    public void AnimateSpawn()
    {
        for (var i = 0; i < 20; ++i)
        {
            var velX = Random.NextGaussian() * 0.02D;
            var velY = Random.NextGaussian() * 0.02D;
            var velZ = Random.NextGaussian() * 0.02D;
            var spread = 10.0D;
            World.Broadcaster.AddParticle("explode", X + Random.NextFloat() * Width * 2.0F - Width - velX * spread, Y + Random.NextFloat() * Height - velY * spread, Z + Random.NextFloat() * Width * 2.0F - Width - velZ * spread, velX, velY,
                velZ);
        }
    }

    public override void TickRiding()
    {
        base.TickRiding();
        LastWalkProgress = WalkProgress;
        WalkProgress = 0.0F;
    }

    public override void SetPositionAndAnglesAvoidEntities(double newPosX, double newPosY, double newPosZ, float newRotationYaw, float newRotationPitch, int newPosRotationIncrements)
    {
        StandingEyeHeight = 0.0F;
        NewPosX = newPosX;
        NewPosY = newPosY;
        NewPosZ = newPosZ;
        NewRotationYaw = newRotationYaw;
        NewRotationPitch = newRotationPitch;
        NewPosRotationIncrements = newPosRotationIncrements;
    }

    public override void Tick()
    {
        base.Tick();
        TickMovement();

        // After the movement tick, not inside it: TickMovement has early returns, and this must run
        // whichever path it took.
        Physics?.AfterTickMovement(this);

        var dx = X - PrevX;
        var dz = Z - PrevZ;
        var horizontalDistance = MathHelper.Sqrt(dx * dx + dz * dz);
        var computedYaw = BodyYaw;
        var walkSpeed = 0.0F;
        LastWalkProgress = WalkProgress;
        var walkAmount = 0.0F;
        if (horizontalDistance > 0.05F)
        {
            walkAmount = 1.0F;
            walkSpeed = horizontalDistance * 3.0F;
            computedYaw = (float)Math.Atan2(dz, dx) * 180.0F / (float)Math.PI - 90.0F;
        }

        if (SwingAnimationProgress > 0.0F)
        {
            computedYaw = Yaw;
        }

        if (!OnGround)
        {
            walkAmount = 0.0F;
        }

        WalkProgress += (walkAmount - WalkProgress) * 0.3F;

        var yawDelta = computedYaw - BodyYaw;
        while (yawDelta < -180.0F)
        {
            yawDelta += 360.0F;
        }

        while (yawDelta >= 180.0F)
        {
            yawDelta -= 360.0F;
        }

        BodyYaw += yawDelta * 0.3F;

        var headYawDelta = Yaw - BodyYaw;
        while (headYawDelta < -180.0F)
        {
            headYawDelta += 360.0F;
        }

        while (headYawDelta >= 180.0F)
        {
            headYawDelta -= 360.0F;
        }

        var headFacingBackward = headYawDelta < -90.0F || headYawDelta >= 90.0F;

        if (headYawDelta < -75.0F)
        {
            headYawDelta = -75.0F;
        }

        if (headYawDelta >= 75.0F)
        {
            headYawDelta = 75.0F;
        }

        BodyYaw = Yaw - headYawDelta;

        if (headYawDelta * headYawDelta > 2500.0F)
        {
            BodyYaw += headYawDelta * 0.2F;
        }

        if (headFacingBackward)
        {
            walkSpeed *= -1.0F;
        }

        while (Yaw - PrevYaw < -180.0F)
        {
            PrevYaw -= 360.0F;
        }

        while (Yaw - PrevYaw >= 180.0F)
        {
            PrevYaw += 360.0F;
        }

        while (BodyYaw - LastBodyYaw < -180.0F)
        {
            LastBodyYaw -= 360.0F;
        }

        while (BodyYaw - LastBodyYaw >= 180.0F)
        {
            LastBodyYaw += 360.0F;
        }

        while (Pitch - PrevPitch < -180.0F)
        {
            PrevPitch -= 360.0F;
        }

        while (Pitch - PrevPitch >= 180.0F)
        {
            PrevPitch += 360.0F;
        }

        TotalWalkDistance += walkSpeed;

        Ticker?.OnTickEnd(this);
    }

    protected internal override void SetBoundingBoxSpacing(float widthOffset, float heightOffset) => base.SetBoundingBoxSpacing(widthOffset, heightOffset);

    public virtual void Heal(int amount)
    {
        if (Health <= 0)
        {
            return;
        }

        Health += amount;
        if (Health > MaxHealth)
        {
            Health = MaxHealth;
        }

        Hearts = MaxHealth / 2;
    }

    public override bool Damage(Entity? entity, int amount)
    {
        if (World.IsRemote)
        {
            return false;
        }

        Behaviors.Lifecycle?.OnDamaged(this, entity, amount);
        if (Behaviors.Lifecycle is { } lifecycle)
        {
            amount = lifecycle.ModifyDamage(this, entity, amount);
        }

        EntityAge = 0;
        if (Health <= 0)
        {
            return false;
        }

        WalkAnimationSpeed = 1.5F;
        var playHurtEffects = true;
        if (Hearts > MaxHealth / 2.0F)
        {
            if (amount <= DamageForDisplay)
            {
                return false;
            }

            ApplyDamage(amount - DamageForDisplay);
            DamageForDisplay = amount;
            playHurtEffects = false;
        }
        else
        {
            DamageForDisplay = amount;
            LastHealth = Health;
            Hearts = MaxHealth;
            ApplyDamage(amount);
            HurtTime = MaxHurtTime = 10;
        }

        AttackedAtYaw = 0.0F;
        if (playHurtEffects)
        {
            World.Broadcaster.EntityEvent(this, EntityStatusMessage.EntityState.Hurt);
            ScheduleVelocityUpdate();
            if (entity != null)
            {
                var knockbackX = entity.X - X;

                double knockbackZ;
                for (knockbackZ = entity.Z - Z; knockbackX * knockbackX + knockbackZ * knockbackZ < 1.0E-4D; knockbackZ = (System.Random.Shared.NextDouble() - System.Random.Shared.NextDouble()) * 0.01D)
                {
                    knockbackX = (System.Random.Shared.NextDouble() - System.Random.Shared.NextDouble()) * 0.01D;
                }

                AttackedAtYaw = (float)(Math.Atan2(knockbackZ, knockbackX) * 180.0D / (float)Math.PI) - Yaw;
                KnockBack(entity, amount, knockbackX, knockbackZ);
            }
            else
            {
                AttackedAtYaw = (int)(System.Random.Shared.NextDouble() * 2.0D) * 180;
            }
        }

        if (Health <= 0)
        {
            if (playHurtEffects)
            {
                if (DeathSound != null)
                {
                    World.Broadcaster.PlaySoundAtEntity(this, DeathSound, SoundVolume, (Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F);
                }
            }

            OnKilledBy(entity);
        }
        else if (playHurtEffects)
        {
            if (HurtSound != null)
            {
                World.Broadcaster.PlaySoundAtEntity(this, HurtSound, SoundVolume, (Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F);
            }
        }

        Behaviors.Lifecycle?.OnDamageApplied(this, entity, amount);
        GameEvents.PublishEntityHurt(new EntityHurtEvent(ID, entity?.ID, amount));
        return true;
    }

    public override void AnimateHurt()
    {
        HurtTime = MaxHurtTime = 10;
        AttackedAtYaw = 0.0F;
    }

    protected virtual void ApplyDamage(int amount) => Health -= amount;

    private void KnockBack(Entity entity, int amount, double dx, double dy)
    {
        var knockbackLength = MathHelper.Sqrt(dx * dx + dy * dy);
        const float knockbackStrength = 0.4F;
        VelocityX /= 2.0D;
        VelocityY /= 2.0D;
        VelocityZ /= 2.0D;
        VelocityX -= dx / knockbackLength * knockbackStrength;
        VelocityY += 0.4F;
        VelocityZ -= dy / knockbackLength * knockbackStrength;
        if (VelocityY > 0.4F)
        {
            VelocityY = 0.4F;
        }
    }

    protected virtual void OnKilledBy(Entity? entity)
    {
        if (ScoreAmount >= 0 && entity != null)
        {
            entity.UpdateKilledAchievement(this, ScoreAmount);
        }

        entity?.OnKillOther(this);

        if (!World.IsRemote)
        {
            DropLoot(entity);
        }

        World.Broadcaster.EntityEvent(this, EntityStatusMessage.EntityState.Death);
    }

    protected virtual void DropLoot(Entity? killer) => Loot?.DropLoot(this, killer);

    public override void MarkDead()
    {
        Lifecycle?.OnMarkDead(this);
        base.MarkDead();
    }

    public override void OnStruckByLightning(Entity bolt)
    {
        if (Lifecycle?.OnStruckByLightning(this, bolt) == true)
        {
            return;
        }

        base.OnStruckByLightning(bolt);
    }

    protected override void OnLanding(float fallDistance)
    {
        if (Physics?.OnLanding(this, fallDistance) == true)
        {
            return;
        }

        base.OnLanding(fallDistance);
        var fallDamage = (int)Math.Ceiling(fallDistance - 3.0F);
        if (fallDamage <= 0)
        {
            return;
        }

        Damage(null, fallDamage);
        var groundBlockId = World.Reader.GetBlockId(MathHelper.Floor(X), MathHelper.Floor(Y - 0.2F - StandingEyeHeight), MathHelper.Floor(Z));
        if (groundBlockId <= 0)
        {
            return;
        }

        var soundGroup = World.Content.Blocks.GetByProtocolId(groundBlockId).SoundGroup;
        World.Broadcaster.PlaySoundAtEntity(this, soundGroup.StepSound, soundGroup.Volume * 0.5F, soundGroup.Pitch * (12.0F / 16.0F));
    }

    protected virtual void Travel(float strafe, float forward)
    {
        if (Physics?.Travel(this, strafe, forward) == true)
        {
            return;
        }

        double previousY;
        if (IsInWater)
        {
            previousY = Y;
            MoveNonSolid(strafe, forward, 0.02F);
            Move(VelocityX, VelocityY, VelocityZ);
            VelocityX *= 0.8F;
            VelocityY *= 0.8F;
            VelocityZ *= 0.8F;
            VelocityY -= 0.02D;
            if (HorizontalCollision && GetEntitiesInside(VelocityX, VelocityY + 0.6F - Y + previousY, VelocityZ))
            {
                VelocityY = 0.3F;
            }
        }
        else if (IsTouchingLava)
        {
            previousY = Y;
            MoveNonSolid(strafe, forward, 0.02F);
            Move(VelocityX, VelocityY, VelocityZ);
            VelocityX *= 0.5D;
            VelocityY *= 0.5D;
            VelocityZ *= 0.5D;
            VelocityY -= 0.02D;
            if (HorizontalCollision && GetEntitiesInside(VelocityX, VelocityY + 0.6F - Y + previousY, VelocityZ))
            {
                VelocityY = 0.3F;
            }
        }
        else
        {
            var friction = 0.91F;
            if (OnGround)
            {
                friction = 546.0F * 0.1F * 0.1F * 0.1F;
                var groundBlockId = World.Reader.GetBlockId(MathHelper.Floor(X), MathHelper.Floor(BoundingBox.MinY) - 1, MathHelper.Floor(Z));
                if (groundBlockId > 0)
                {
                    friction = World.Content.Blocks.GetByProtocolId(groundBlockId).Slipperiness * 0.91F;
                }
            }

            var movementFactor = 0.16277136F / (friction * friction * friction);
            MoveNonSolid(strafe, forward, OnGround ? 0.1F * movementFactor : AirSpeed);
            friction = 0.91F;
            if (OnGround)
            {
                friction = 546.0F * 0.1F * 0.1F * 0.1F;
                var groundBlockId = World.Reader.GetBlockId(MathHelper.Floor(X), MathHelper.Floor(BoundingBox.MinY) - 1, MathHelper.Floor(Z));
                if (groundBlockId > 0)
                {
                    friction = World.Content.Blocks.GetByProtocolId(groundBlockId).Slipperiness * 0.91F;
                }
            }

            if (IsOnLadder)
            {
                const float ladderSpeedClamp = 0.15F;
                if (VelocityX < -ladderSpeedClamp)
                {
                    VelocityX = -ladderSpeedClamp;
                }

                if (VelocityX > ladderSpeedClamp)
                {
                    VelocityX = ladderSpeedClamp;
                }

                if (VelocityZ < -ladderSpeedClamp)
                {
                    VelocityZ = -ladderSpeedClamp;
                }

                if (VelocityZ > ladderSpeedClamp)
                {
                    VelocityZ = ladderSpeedClamp;
                }

                FallDistance = 0.0F;
                if (VelocityY < -0.15D)
                {
                    VelocityY = -0.15D;
                }

                if (IsSneaking() && VelocityY < 0.0D)
                {
                    VelocityY = 0.0D;
                }
            }

            Move(VelocityX, VelocityY, VelocityZ);
            if (HorizontalCollision && IsOnLadder)
            {
                VelocityY = 0.2D;
            }

            VelocityY -= 0.08D;
            VelocityY *= 0.98F;
            VelocityX *= friction;
            VelocityZ *= friction;
        }

        LastWalkAnimationSpeed = WalkAnimationSpeed;
        previousY = X - PrevX;
        var deltaZ = Z - PrevZ;
        var distanceMoved = MathHelper.Sqrt(previousY * previousY + deltaZ * deltaZ) * 4.0F;
        if (distanceMoved > 1.0F)
        {
            distanceMoved = 1.0F;
        }

        WalkAnimationSpeed += (distanceMoved - WalkAnimationSpeed) * 0.4F;
        AnimationPhase += WalkAnimationSpeed;
    }

    protected override void WriteNbt(NBTTagCompound nbt)
    {
        nbt.SetShort("Health", (short)Health);
        nbt.SetShort("HurtTime", (short)HurtTime);
        nbt.SetShort("DeathTime", (short)DeathTime);
        nbt.SetShort("AttackTime", (short)AttackTime);
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        Health = nbt.GetShort("Health");
        if (!nbt.HasKey("Health"))
        {
            Health = 10;
        }

        HurtTime = nbt.GetShort("HurtTime");
        DeathTime = nbt.GetShort("DeathTime");
        AttackTime = nbt.GetShort("AttackTime");
    }

    protected virtual bool canBreatheUnderwater() => Definition.BreathesUnderwater;

    protected virtual void TickMovement()
    {
        Ticker?.OnTickMovement(this);

        if (World.IsRemote && this is not EntityPlayer)
        {
            var minChunkX = MathHelper.Floor(BoundingBox.MinX) >> 4;
            var maxChunkX = MathHelper.Floor(BoundingBox.MaxX) >> 4;
            var minChunkZ = MathHelper.Floor(BoundingBox.MinZ) >> 4;
            var maxChunkZ = MathHelper.Floor(BoundingBox.MaxZ) >> 4;

            for (var chunkX = minChunkX; chunkX <= maxChunkX; ++chunkX)
            {
                for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; ++chunkZ)
                {
                    var chunk = World.ChunkHost.GetChunk(chunkX, chunkZ);
                    if (chunk.Loaded)
                    {
                        continue;
                    }

                    Jumping = false;
                    SidewaysSpeed = 0.0F;
                    ForwardSpeed = 0.0F;
                    RotationSpeed = 0.0F;
                    VelocityX = VelocityY = VelocityZ = 0.0D;
                    return;
                }
            }
        }

        if (NewPosRotationIncrements > 0)
        {
            var newX = X + (NewPosX - X) / NewPosRotationIncrements;
            var newY = Y + (NewPosY - Y) / NewPosRotationIncrements;
            var newZ = Z + (NewPosZ - Z) / NewPosRotationIncrements;

            double yawDelta;
            for (yawDelta = NewRotationYaw - Yaw; yawDelta < -180.0D; yawDelta += 360.0D)
            {
            }

            while (yawDelta >= 180.0D)
            {
                yawDelta -= 360.0D;
            }

            Yaw = (float)(Yaw + yawDelta / NewPosRotationIncrements);
            Pitch = (float)(Pitch + (NewRotationPitch - Pitch) / NewPosRotationIncrements);
            --NewPosRotationIncrements;
            SetPosition(newX, newY, newZ);
            SetRotation(Yaw, Pitch);
            var collisions = World.Entities.GetEntityCollisionsScratch(this, BoundingBox.Contract(1.0D / 32.0D, 0.0D, 1.0D / 32.0D));
            if (collisions.Count > 0)
            {
                var highestCollisionY = BoundingBox.MinY;
                var applyStep = false;

                foreach (var col in collisions)
                {
                    if (!(col.MaxY > highestCollisionY) || !(col.MaxY <= BoundingBox.MinY + 1.0))
                    {
                        continue;
                    }

                    highestCollisionY = col.MaxY;
                    applyStep = true;
                }

                if (applyStep)
                {
                    newY += highestCollisionY - BoundingBox.MinY;
                }

                SetPosition(newX, newY, newZ);
            }
        }

        if (isMovementBlocked())
        {
            Jumping = false;
            SidewaysSpeed = 0.0F;
            ForwardSpeed = 0.0F;
            RotationSpeed = 0.0F;
        }
        else if (!InterpolateOnly)
        {
            TickLiving();

            // After the AI, not inside it: a creature's TickLiving is its pathfinding.
            Ticker?.AfterTickLiving(this);
        }

        var isInWater = InWater;
        var isTouchingLava = IsTouchingLava;
        if (Jumping)
        {
            if (isInWater || isTouchingLava)
            {
                VelocityY += 0.04F;
            }
            else if (OnGround)
            {
                Jump();
            }
        }

        SidewaysSpeed *= 0.98F;
        ForwardSpeed *= 0.98F;
        RotationSpeed *= 0.9F;
        Travel(SidewaysSpeed, ForwardSpeed);
        var nearbyEntities = World.Entities.GetEntitiesScratch(this, BoundingBox.Expand(0.2F, 0.0D, 0.2F));
        if (nearbyEntities.Count <= 0)
        {
            return;
        }

        foreach (var entity in nearbyEntities)
        {
            if (entity.IsPushable)
            {
                entity.OnCollision(this);
            }
        }
    }

    protected virtual bool isMovementBlocked() => Health <= 0;

    protected virtual void Jump() => VelocityY = 0.42F;

    /// <summary>
    ///     Despawns the mob once no player is near enough to keep it, and ages out one that has been
    ///     idle too long. Reachable from behaviors, since a ticker replacing the AI still has to run it.
    /// </summary>
    protected internal void TickDespawn()
    {
        var player = World.Entities.GetClosestPlayer(X, Y, Z, -1.0D);
        if (!CanDespawn || player == null)
        {
            return;
        }

        var dx = player.X - X;
        var dy = player.Y - Y;
        var dz = player.Z - Z;
        var squaredDistance = dx * dx + dy * dy + dz * dz;
        if (squaredDistance > 16384.0D)
        {
            MarkDead();
        }

        if (EntityAge <= 600 || Random.NextInt(800) != 0)
        {
            return;
        }

        if (squaredDistance < 1024.0D)
        {
            EntityAge = 0;
        }
        else
        {
            MarkDead();
        }
    }

    protected virtual void TickLiving()
    {
        // A ticker answering true is the mob's whole AI, so none of the idle logic below runs: a
        // ghast neither ages nor glances around.
        if (Ticker?.OnTickLiving(this) == true)
        {
            return;
        }

        ++EntityAge;
        TickDespawn();
        SidewaysSpeed = 0.0F;
        ForwardSpeed = 0.0F;
        const float lookRange = 8.0F;
        if (Random.NextFloat() < 0.02F)
        {
            var closestPlayer = World.Entities.GetClosestPlayer(X, Y, Z, lookRange);
            if (closestPlayer != null)
            {
                CurrentTarget = closestPlayer;
                LookTimer = 10 + Random.NextInt(20);
            }
            else
            {
                RotationSpeed = (Random.NextFloat() - 0.5F) * 20.0F;
            }
        }

        if (CurrentTarget != null)
        {
            faceEntity(CurrentTarget, 10.0F, getMaxFallDistance());
            if (LookTimer-- <= 0 || CurrentTarget.Dead || CurrentTarget.GetSquaredDistance(this) > lookRange * lookRange)
            {
                CurrentTarget = null;
            }
        }
        else
        {
            if (Random.NextFloat() < 0.05F)
            {
                RotationSpeed = (Random.NextFloat() - 0.5F) * 20.0F;
            }

            Yaw += RotationSpeed;
            Pitch = DefaultPitch;
        }

        var isInWater = InWater;
        var isTouchingLava = IsTouchingLava;
        if (isInWater || isTouchingLava)
        {
            Jumping = Random.NextFloat() < 0.8F;
        }
    }

    protected virtual int getMaxFallDistance() => Physics?.MaxFallDistance(this) ?? 40;

    protected internal void faceEntity(Entity entity, float yawSpeed, float pitchSpeed)
    {
        var dx = entity.X - X;
        var dz = entity.Z - Z;
        double dy;
        if (entity is EntityLiving living)
        {
            dy = Y + EyeHeight - (living.Y + living.EyeHeight);
        }
        else
        {
            dy = (entity.BoundingBox.MinY + entity.BoundingBox.MaxY) / 2.0D - (Y + EyeHeight);
        }

        double horizontalDistance = MathHelper.Sqrt(dx * dx + dz * dz);
        var targetYaw = (float)(Math.Atan2(dz, dx) * 180.0D / (float)Math.PI) - 90.0F;
        var targetPitch = (float)-(Math.Atan2(dy, horizontalDistance) * 180.0D / (float)Math.PI);
        Pitch = -UpdateRotation(Pitch, targetPitch, pitchSpeed);
        Yaw = UpdateRotation(Yaw, targetYaw, yawSpeed);
    }

    private static float UpdateRotation(float currentRotation, float targetRotation, float maxDelta)
    {
        var delta = targetRotation - currentRotation;

        while (delta < -180.0F)
        {
            delta += 360.0F;
        }

        while (delta >= 180.0F)
        {
            delta -= 360.0F;
        }

        if (delta > maxDelta)
        {
            delta = maxDelta;
        }

        if (delta < -maxDelta)
        {
            delta = -maxDelta;
        }

        return currentRotation + delta;
    }

    private static void OnEntityDeath()
    {
    }

    /// <summary>
    ///     Whether the mob may spawn where it stands. A Physics slot declaring its own rule replaces
    ///     this one outright: a zombie pigman ignores darkness, a ghast spawns one time in twenty.
    /// </summary>
    public bool CanSpawn() => Physics?.CanSpawn(this) ?? CanSpawnHere();

    /// <summary>The type's own placement rule: the body fits, nothing is in the way, no fluid.</summary>
    protected virtual bool CanSpawnHere() => World.Entities.CanSpawnEntity(BoundingBox) && World.Entities.GetEntityCollisionsScratch(this, BoundingBox).Count == 0 && !World.Reader.IsMaterialInBox(BoundingBox, m => m.IsFluid);

    protected override void TickInVoid() => Damage(null, 4);

    public float GetSwingProgress(float partialTick)
    {
        var progressDelta = SwingAnimationProgress - LastSwingAnimationProgress;
        if (progressDelta < 0.0F)
        {
            ++progressDelta;
        }

        return LastSwingAnimationProgress + progressDelta * partialTick;
    }

    public Vec3D GetPosition() => new(X, Y, Z);

    public Vec3D GetPosition(float partialTick)
    {
        if (Math.Abs(partialTick - 1.0F) < 0.01f)
        {
            return new Vec3D(X, Y, Z);
        }

        var x = PrevX + (X - PrevX) * partialTick;
        var y = PrevY + (Y - PrevY) * partialTick;
        var z = PrevZ + (Z - PrevZ) * partialTick;
        return new Vec3D(x, y, z);
    }

    public Vec3D GetLook(float partialTick)
    {
        float cosYaw;
        float sinYaw;
        float cosPitch;
        float sinPitch;
        if (Math.Abs(partialTick - 1.0F) < 0.01f)
        {
            cosYaw = MathHelper.Cos(-Yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
            sinYaw = MathHelper.Sin(-Yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
            cosPitch = -MathHelper.Cos(-Pitch * ((float)Math.PI / 180.0F));
            sinPitch = MathHelper.Sin(-Pitch * ((float)Math.PI / 180.0F));
            return new Vec3D(sinYaw * cosPitch, sinPitch, cosYaw * cosPitch);
        }

        cosYaw = PrevPitch + (Pitch - PrevPitch) * partialTick;
        sinYaw = PrevYaw + (Yaw - PrevYaw) * partialTick;
        cosPitch = MathHelper.Cos(-sinYaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
        sinPitch = MathHelper.Sin(-sinYaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
        var horizontalCos = -MathHelper.Cos(-cosYaw * ((float)Math.PI / 180.0F));
        var verticalSin = MathHelper.Sin(-cosYaw * ((float)Math.PI / 180.0F));
        return new Vec3D(sinPitch * horizontalCos, verticalSin, cosPitch * horizontalCos);
    }

    public HitResult RayTrace(double range, float partialTick)
    {
        var startPos = GetPosition(partialTick);
        var lookDir = GetLook(partialTick);
        var endPos = startPos + range * lookDir;
        return World.Reader.Raycast(startPos, endPos);
    }

    public override void ProcessServerEntityStatus(sbyte statusId)
    {
        switch (statusId)
        {
            case (sbyte)EntityStatusMessage.EntityState.Hurt:
                WalkAnimationSpeed = 1.5F;
                Hearts = MaxHealth;
                HurtTime = MaxHurtTime = 10;
                AttackedAtYaw = 0.0F;
                if (HurtSound != null)
                {
                    World.Broadcaster.PlaySoundAtEntity(this, HurtSound, SoundVolume, (Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F);
                }

                Damage(null, 0);
                break;
            case (sbyte)EntityStatusMessage.EntityState.Death:
                if (DeathSound != null)
                {
                    World.Broadcaster.PlaySoundAtEntity(this, DeathSound, SoundVolume, (Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F);
                }

                Health = 0;
                OnKilledBy(null);
                break;
            default:
                if (Behaviors.Lifecycle?.OnEntityStatus(this, statusId) == true)
                {
                    break;
                }

                base.ProcessServerEntityStatus(statusId);
                break;
        }
    }

    public virtual int GetItemStackTextureId(ItemStack item) => item.GetTextureId();
}
