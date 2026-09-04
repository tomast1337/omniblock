using OmniBlock.Entities.State;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.NBT;
using OmniBlock.Network.Messages;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A mob that can be tamed with an item, healed by feeding, told to sit, and angered by being
///     hit. Fills six slots from one entry because all six read the same three bits, sitting, angry,
///     tamed, packed into one synced byte. The packing is a protocol fact: the client reads all
///     three out of a single metadata index.
/// </summary>
public sealed class TameableBehavior : IEntityInteractable, IEntityPersistence, IEntityTargetBehavior, IEntityTicker, IEntityLifecycle, IEntityPhysics
{
    private const byte SittingBit = 1;
    private const byte AngryBit = 2;
    private const byte TamedBit = 4;
    private readonly string _angrySound;
    private readonly string _angryTexture;
    private readonly string _contentSound;
    private readonly int _feedHealAmount;

    private readonly SyncedHandle<byte> _flags;
    private readonly string _idleSound;
    private readonly SyncedHandle<string?> _owner;
    private readonly double _packRadius;
    private readonly SyncedHandle<int> _shownHealth;
    private readonly int _sittingWatchfulness;
    private readonly int _tamedHealth;

    private readonly string _tamedTexture;
    private readonly int _tamingChanceOneIn;

    private readonly Item _tamingItem;
    private readonly IEntityTargetBehavior _whenAngry;
    private readonly int _whineBelowHealth;
    private readonly string _whineSound;

    public TameableBehavior(in EntityBehaviorContext context)
    {
        _tamingItem = context.Items.Get(ResourceLocation.Parse(context.Json.GetProperty("taming_item").GetString()!));
        _tamingChanceOneIn = context.Int("taming_chance_one_in", 3);
        _tamedHealth = context.Int("tamed_health", 20);
        _feedHealAmount = context.Items.Get(ResourceLocation.Parse(context.Json.GetProperty("feed_heal_like").GetString()!))
            .GetBehavior<FoodBehavior>()!.HealAmount;

        _tamedTexture = context.Json.GetProperty("tamed_texture").GetString()!;
        _angryTexture = context.Json.GetProperty("angry_texture").GetString()!;
        _angrySound = context.Json.GetProperty("angry_sound").GetString()!;
        _whineSound = context.Json.GetProperty("whine_sound").GetString()!;
        _contentSound = context.Json.GetProperty("content_sound").GetString()!;
        _idleSound = context.Json.GetProperty("idle_sound").GetString()!;
        _whineBelowHealth = context.Int("whine_below_health", 10);
        _sittingWatchfulness = context.Int("sitting_watchfulness", 20);
        _packRadius = context.Double("pack_radius", 16.0D);

        _flags = context.Synced<byte>("flags");
        _owner = context.Synced<string?>("owner");
        _shownHealth = context.Synced<int>("shown_health");

        _whenAngry = context.Json.TryGetProperty("when_angry", out var angry)
            ? (IEntityTargetBehavior)context.Build(angry)
            : new AlwaysHuntTargetBehavior();
    }

    // Interactable

    public bool OnInteract(Entity self, EntityPlayer player)
    {
        if (self is not EntityCreature mob)
        {
            return false;
        }

        return IsTamed(mob) ? InteractWithPet(mob, player) : TryTame(mob, player);
    }

    // Lifecycle

    /// <summary>Any damage attempt stands the mob up, whether or not it lands.</summary>
    public void OnDamaged(EntityLiving self, Entity? attacker, int amount) => SetSitting(self, false);

    /// <summary>Halves damage from anything but a player's own hand or arrow.</summary>
    public int ModifyDamage(EntityLiving self, Entity? attacker, int amount) =>
        attacker is null or EntityPlayer || ArrowBehavior.IsArrow(attacker) ? amount : (amount + 1) / 2;

    public void OnDamageApplied(EntityLiving self, Entity? attacker, int amount)
    {
        if (self is not EntityCreature creature)
        {
            return;
        }

        if (!IsTamed(self) && !IsAngry(self))
        {
            RousePack(creature, attacker);
        }
        else if (attacker != null && !Equals(attacker, self))
        {
            DefendSelf(creature, attacker);
        }
    }

    public bool OnEntityStatus(EntityLiving self, sbyte status)
    {
        switch ((EntityStatusMessage.EntityState)status)
        {
            case EntityStatusMessage.EntityState.WolfHeartsFx:
                ShowParticles(self, "heart");
                return true;
            case EntityStatusMessage.EntityState.WolfSmokeFx:
                ShowParticles(self, "smoke");
                return true;
            default:
                return false;
        }
    }

    // Persistence

    /// <summary>A tamed mob is never despawned.</summary>
    public bool? CanDespawn(EntityLiving self) => !IsTamed(self);

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        nbt.SetBoolean("Angry", IsAngry(self));
        nbt.SetBoolean("Sitting", IsSitting(self));
        nbt.SetString("Owner", Owner(self) ?? "");
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        SetAngry(self, nbt.GetBoolean("Angry"));
        SetSitting(self, nbt.GetBoolean("Sitting"));

        // An owner name on disk is the only record that the mob was ever tamed.
        var owner = nbt.GetString("Owner");
        if (owner.Length <= 0)
        {
            return;
        }

        SetOwner(self, owner);
        SetTamed(self, true);
    }

    // Physics

    public bool? IsMovementCeased(EntityLiving self) => IsSitting(self) ? true : null;

    public int? MaxFallDistance(EntityLiving self) => IsSitting(self) ? _sittingWatchfulness : null;

    // Targeting

    /// <summary>Hunts only while angry; a tamed or calm mob picks no target of its own.</summary>
    public Entity? FindPlayerToAttack(EntityCreature self) => IsAngry(self) ? _whenAngry.FindPlayerToAttack(self) : null;

    // Ticker

    /// <summary>Runs after the AI: getting wet stands the mob up, and the shown health is republished.</summary>
    public void AfterTickLiving(EntityLiving self)
    {
        if (self.IsInWater)
        {
            SetSitting(self, false);
        }

        if (!self.World.IsRemote)
        {
            self.DataSynchronizer.Get<int>(_shownHealth.Id).Value = self.Health;
        }
    }

    /// <summary>Tamed and angry each swap the texture.</summary>
    public void OnTickEnd(EntityLiving self) =>
        self.Texture = IsTamed(self) ? _tamedTexture : IsAngry(self) ? _angryTexture : self.Definition.Texture;

    public string? LivingSound(EntityLiving self)
    {
        if (IsAngry(self))
        {
            return _angrySound;
        }

        if (self.Random.NextInt(3) != 0)
        {
            return _idleSound;
        }

        return IsTamed(self) && ShownHealth(self) < _whineBelowHealth ? _whineSound : _contentSound;
    }

    private byte Flags(Entity self) => self.DataSynchronizer.Get<byte>(_flags.Id).Value;

    private void SetFlag(Entity self, byte bit, bool on)
    {
        var flags = self.DataSynchronizer.Get<byte>(_flags.Id);
        flags.Value = on ? (byte)(flags.Value | bit) : (byte)(flags.Value & ~bit);
    }

    public bool IsSitting(Entity self) => (Flags(self) & SittingBit) != 0;
    public bool IsAngry(Entity self) => (Flags(self) & AngryBit) != 0;
    public bool IsTamed(Entity self) => (Flags(self) & TamedBit) != 0;

    public void SetSitting(Entity self, bool sitting) => SetFlag(self, SittingBit, sitting);
    private void SetAngry(Entity self, bool angry) => SetFlag(self, AngryBit, angry);
    private void SetTamed(Entity self, bool tamed) => SetFlag(self, TamedBit, tamed);

    public string? Owner(Entity self) => self.DataSynchronizer.Get<string?>(_owner.Id).Value;
    private void SetOwner(Entity self, string? owner) => self.DataSynchronizer.Get<string?>(_owner.Id).Value = owner;

    /// <summary>Health as the client last heard it. The tail angle is drawn from this.</summary>
    public int ShownHealth(Entity self) => self.DataSynchronizer.Get<int>(_shownHealth.Id).Value;

    public bool IsOwnedBy(Entity self, EntityPlayer player) =>
        player.Name != null && player.Name.Equals(Owner(self), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     An untamed pack targets whatever drew blood, but only turns angry if that was a player.
    ///     An arrow is credited to whoever fired it.
    /// </summary>
    private void RousePack(EntityCreature self, Entity? attacker)
    {
        var byTargetablePlayer = attacker is EntityPlayer { GameMode.CanBeTargeted: true };
        if (byTargetablePlayer)
        {
            SetAngry(self, true);
            self.Target = attacker;
        }

        if (ArrowBehavior.OwnerOf(attacker) is { } shooter)
        {
            attacker = shooter;
        }

        if (attacker is not EntityLiving)
        {
            return;
        }

        // Not excluding self: an untargetable attacker leaves this mob without a target, and the
        // loop below is what gives it one.
        foreach (var nearby in self.World.Entities.GetEntities(null, self.BoundingBox.Expand(_packRadius, 4.0D, _packRadius)))
        {
            // Same behavior instance means same entity type, so only its own kind joins in.
            if (nearby is not EntityCreature pack || !ReferenceEquals(pack.Behaviors.Find<TameableBehavior>(), this))
            {
                continue;
            }

            if (IsTamed(pack) || pack.Target != null)
            {
                continue;
            }

            pack.Target = attacker;
            if (byTargetablePlayer)
            {
                SetAngry(pack, true);
            }
        }
    }

    /// <summary>A tamed mob never targets its own owner.</summary>
    private void DefendSelf(EntityCreature self, Entity attacker)
    {
        if (IsTamed(self) && attacker is EntityPlayer { GameMode.CanBeTargeted: false } player && IsOwnedBy(self, player))
        {
            return;
        }

        self.Target = attacker;
    }

    private bool TryTame(EntityCreature self, EntityPlayer player)
    {
        var held = player.Inventory.ItemInHand;
        if (held == null || held.ItemId != _tamingItem.Id || IsAngry(self))
        {
            return false;
        }

        Consume(held, player);
        if (self.World.IsRemote)
        {
            return true;
        }

        if (self.Random.NextInt(_tamingChanceOneIn) != 0)
        {
            ShowParticles(self, "smoke");
            self.World.Broadcaster.EntityEvent(self, EntityStatusMessage.EntityState.WolfSmokeFx);
            return true;
        }

        SetTamed(self, true);
        self.setPathToEntity(null);
        SetSitting(self, true);
        self.Health = _tamedHealth;
        SetOwner(self, player.Name);
        ShowParticles(self, "heart");
        self.World.Broadcaster.EntityEvent(self, EntityStatusMessage.EntityState.WolfHeartsFx);
        return true;
    }

    private bool InteractWithPet(EntityCreature self, EntityPlayer player)
    {
        var held = player.Inventory.ItemInHand;
        if (held != null
            && held.GetItem().GetBehavior<FoodBehavior>() is { IsMeat: true }
            && ShownHealth(self) < _tamedHealth)
        {
            Consume(held, player);
            self.Heal(_feedHealAmount);
            return true;
        }

        // Only the owner can toggle sitting.
        if (player.Name != null && !IsOwnedBy(self, player))
        {
            return false;
        }

        if (self.World.IsRemote)
        {
            return true;
        }

        SetSitting(self, !IsSitting(self));
        self.Jumping = false;
        self.setPathToEntity(null);
        return true;
    }

    private static void Consume(ItemStack held, EntityPlayer player)
    {
        held.ConsumeItem(player);
        if (held.Count <= 0)
        {
            player.Inventory.SetStack(player.Inventory.SelectedSlot, null);
        }
    }

    private static void ShowParticles(Entity self, string particle)
    {
        for (var i = 0; i < 7; ++i)
        {
            var driftX = self.Random.NextGaussian() * 0.02D;
            var driftY = self.Random.NextGaussian() * 0.02D;
            var driftZ = self.Random.NextGaussian() * 0.02D;
            self.World.Broadcaster.AddParticle(
                particle,
                self.X + self.Random.NextFloat() * self.Width * 2.0F - self.Width,
                self.Y + 0.5D + self.Random.NextFloat() * self.Height,
                self.Z + self.Random.NextFloat() * self.Width * 2.0F - self.Width,
                driftX, driftY, driftZ);
        }
    }

    /// <summary>Tail angle for the renderer: up when angry, low when calm, health-scaled when tamed.</summary>
    public float TailRotation(Entity self) =>
        IsAngry(self) ? (float)Math.PI * 0.49F
        : IsTamed(self) ? (0.55F - (_tamedHealth - ShownHealth(self)) * 0.02F) * (float)Math.PI
        : (float)Math.PI * 0.2F;
}
