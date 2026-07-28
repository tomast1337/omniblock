using System.Text.Json;
using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.Items.Behaviors;
using BetaSharp.NBT;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Util;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A mob that can be won over: fed a bribe until it accepts an owner, then fed to heal, told to
///     sit, and made angry by anyone who hits it. Six slots from one entry, because every one of them
///     reads the same three bits — sitting, angry, tamed — packed into a single synced byte.
///     <para>
///         The bits are packed rather than declared as three properties because the packing is a
///         protocol fact: the client reads them out of one metadata index.
///     </para>
/// </summary>
public sealed class TameableBehavior : IEntityInteractable, IEntityPersistence, IEntityTargetBehavior, IEntityTicker, IEntityLifecycle, IEntityPhysics
{
    private const byte SittingBit = 1;
    private const byte AngryBit = 2;
    private const byte TamedBit = 4;

    private readonly SyncedHandle<byte> _flags;
    private readonly SyncedHandle<string?> _owner;
    private readonly SyncedHandle<int> _shownHealth;

    private readonly Item _tamingItem;
    private readonly int _tamingChanceOneIn;
    private readonly int _tamedHealth;
    private readonly int _feedHealAmount;
    private readonly IEntityTargetBehavior _whenAngry;

    private readonly string _tamedTexture;
    private readonly string _angryTexture;
    private readonly string _angrySound;
    private readonly string _whineSound;
    private readonly string _contentSound;
    private readonly string _idleSound;
    private readonly int _whineBelowHealth;
    private readonly int _sittingWatchfulness;
    private readonly double _packRadius;

    public TameableBehavior(in EntityBehaviorContext context)
    {
        _tamingItem = Item.ByName(ResourceLocation.Parse(context.Json.GetProperty("taming_item").GetString()!).Path);
        _tamingChanceOneIn = context.Int("taming_chance_one_in", 3);
        _tamedHealth = context.Int("tamed_health", 20);
        _feedHealAmount = Item.ByName(ResourceLocation.Parse(context.Json.GetProperty("feed_heal_like").GetString()!).Path)
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

        _whenAngry = context.Json.TryGetProperty("when_angry", out JsonElement angry)
            ? (IEntityTargetBehavior)EntityBehaviorRegistry.Build(context with { Json = angry })
            : new AlwaysHuntTargetBehavior(16.0D);
    }

    private byte Flags(Entity self) => self.DataSynchronizer.Get<byte>(_flags.Id).Value;

    private void SetFlag(Entity self, byte bit, bool on)
    {
        SyncedProperty<byte> flags = self.DataSynchronizer.Get<byte>(_flags.Id);
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

    /// <summary>Health as the client last heard it, which is what the tail angle is drawn from.</summary>
    public int ShownHealth(Entity self) => self.DataSynchronizer.Get<int>(_shownHealth.Id).Value;

    public bool IsOwnedBy(Entity self, EntityPlayer player) =>
        player.Name != null && player.Name.Equals(Owner(self), StringComparison.OrdinalIgnoreCase);

    // --- Ticker -------------------------------------------------------------------------------

    /// <summary>
    ///     Runs after the AI: a mob that gets wet stands up, and the health the client draws from is
    ///     republished. Both belong here because both are the mob's own state, not its pathing.
    /// </summary>
    public void AfterTickLiving(EntityLiving self)
    {
        if (self.IsInWater) SetSitting(self, false);
        if (!self.World.IsRemote) self.DataSynchronizer.Get<int>(_shownHealth.Id).Value = self.Health;
    }

    /// <summary>Mood is written into the texture, the way the ghast writes its charge into one.</summary>
    public void OnTickEnd(EntityLiving self) =>
        self.Texture = IsTamed(self) ? _tamedTexture : IsAngry(self) ? _angryTexture : self.Definition.Texture;

    public string? LivingSound(EntityLiving self)
    {
        if (IsAngry(self)) return _angrySound;
        if (self.Random.NextInt(3) != 0) return _idleSound;

        return IsTamed(self) && ShownHealth(self) < _whineBelowHealth ? _whineSound : _contentSound;
    }

    // --- Targeting ----------------------------------------------------------------------------

    /// <summary>Hunts only while angry; a tamed or calm mob picks no target of its own.</summary>
    public Entity? FindPlayerToAttack(EntityCreature self) => IsAngry(self) ? _whenAngry.FindPlayerToAttack(self) : null;

    // --- Physics ------------------------------------------------------------------------------

    public bool? IsMovementCeased(EntityLiving self) => IsSitting(self) ? true : null;

    public int? MaxFallDistance(EntityLiving self) => IsSitting(self) ? _sittingWatchfulness : null;

    // --- Persistence --------------------------------------------------------------------------

    /// <summary>Somebody's pet is nobody's to clean up.</summary>
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
        string owner = nbt.GetString("Owner");
        if (owner.Length <= 0) return;

        SetOwner(self, owner);
        SetTamed(self, true);
    }

    // --- Lifecycle ----------------------------------------------------------------------------

    /// <summary>Anything worth reacting to gets the mob on its feet, hit or not.</summary>
    public void OnDamaged(EntityLiving self, Entity? attacker, int amount) => SetSitting(self, false);

    /// <summary>
    ///     Halves anything but a player's own hand or arrow, which is what keeps a wolf alive long
    ///     enough to be worth taming.
    /// </summary>
    public int ModifyDamage(EntityLiving self, Entity? attacker, int amount) =>
        attacker is null or EntityPlayer or EntityArrow ? amount : (amount + 1) / 2;

    public void OnDamageApplied(EntityLiving self, Entity? attacker, int amount)
    {
        if (self is not EntityCreature creature) return;

        if (!IsTamed(self) && !IsAngry(self)) RousePack(creature, attacker);
        else if (attacker != null && !Equals(attacker, self)) DefendSelf(creature, attacker);
    }

    /// <summary>
    ///     An untouched pack turns on whatever drew blood, and turns properly angry only if it was a
    ///     player — an arrow is credited to whoever loosed it.
    /// </summary>
    private void RousePack(EntityCreature self, Entity? attacker)
    {
        bool byTargetablePlayer = attacker is EntityPlayer { GameMode.CanBeTargeted: true };
        if (byTargetablePlayer)
        {
            SetAngry(self, true);
            self.Target = attacker;
        }

        if (attacker is EntityArrow arrow) attacker = arrow.Owner;
        if (attacker is not EntityLiving) return;

        // Not excluding self: an untargetable attacker leaves this mob without a target of its own,
        // and the same rule below is what gives it one.
        foreach (Entity nearby in self.World.Entities.GetEntities(null, self.BoundingBox.Expand(_packRadius, 4.0D, _packRadius)))
        {
            // Same behavior instance means same type: only its own kind joins in.
            if (nearby is not EntityCreature pack || !ReferenceEquals(pack.Behaviors.Find<TameableBehavior>(), this)) continue;
            if (IsTamed(pack) || pack.Target != null) continue;

            pack.Target = attacker;
            if (byTargetablePlayer) SetAngry(pack, true);
        }
    }

    /// <summary>A tamed mob never turns on its own owner, however clumsy they are.</summary>
    private void DefendSelf(EntityCreature self, Entity attacker)
    {
        if (IsTamed(self) && attacker is EntityPlayer { GameMode.CanBeTargeted: false } player && IsOwnedBy(self, player)) return;

        self.Target = attacker;
    }

    public bool OnEntityStatus(EntityLiving self, sbyte status)
    {
        switch ((EntityStatusS2CPacket.EntityState)status)
        {
            case EntityStatusS2CPacket.EntityState.WolfHeartsFx:
                ShowParticles(self, "heart");
                return true;
            case EntityStatusS2CPacket.EntityState.WolfSmokeFx:
                ShowParticles(self, "smoke");
                return true;
            default:
                return false;
        }
    }

    // --- Interactable -------------------------------------------------------------------------

    public bool OnInteract(Entity self, EntityPlayer player)
    {
        if (self is not EntityCreature mob) return false;

        return IsTamed(mob) ? InteractWithPet(mob, player) : TryTame(mob, player);
    }

    private bool TryTame(EntityCreature self, EntityPlayer player)
    {
        ItemStack? held = player.Inventory.ItemInHand;
        if (held == null || held.ItemId != _tamingItem.Id || IsAngry(self)) return false;

        Consume(held, player);
        if (self.World.IsRemote) return true;

        if (self.Random.NextInt(_tamingChanceOneIn) != 0)
        {
            ShowParticles(self, "smoke");
            self.World.Broadcaster.EntityEvent(self, EntityStatusS2CPacket.EntityState.WolfSmokeFx);
            return true;
        }

        SetTamed(self, true);
        self.setPathToEntity(null);
        SetSitting(self, true);
        self.Health = _tamedHealth;
        SetOwner(self, player.Name);
        ShowParticles(self, "heart");
        self.World.Broadcaster.EntityEvent(self, EntityStatusS2CPacket.EntityState.WolfHeartsFx);
        return true;
    }

    private bool InteractWithPet(EntityCreature self, EntityPlayer player)
    {
        ItemStack? held = player.Inventory.ItemInHand;
        if (held != null
            && Item.ITEMS[held.ItemId]?.GetBehavior<FoodBehavior>() is { IsMeat: true }
            && ShownHealth(self) < _tamedHealth)
        {
            Consume(held, player);
            self.Heal(_feedHealAmount);
            return true;
        }

        // Only its owner can tell it to sit; anyone else is ignored entirely.
        if (player.Name != null && !IsOwnedBy(self, player)) return false;
        if (self.World.IsRemote) return true;

        SetSitting(self, !IsSitting(self));
        self.Jumping = false;
        self.setPathToEntity(null);
        return true;
    }

    private static void Consume(ItemStack held, EntityPlayer player)
    {
        held.ConsumeItem(player);
        if (held.Count <= 0) player.Inventory.SetStack(player.Inventory.SelectedSlot, null);
    }

    private static void ShowParticles(Entity self, string particle)
    {
        for (int i = 0; i < 7; ++i)
        {
            double driftX = self.Random.NextGaussian() * 0.02D;
            double driftY = self.Random.NextGaussian() * 0.02D;
            double driftZ = self.Random.NextGaussian() * 0.02D;
            self.World.Broadcaster.AddParticle(
                particle,
                self.X + self.Random.NextFloat() * self.Width * 2.0F - self.Width,
                self.Y + 0.5D + self.Random.NextFloat() * self.Height,
                self.Z + self.Random.NextFloat() * self.Width * 2.0F - self.Width,
                driftX, driftY, driftZ);
        }
    }

    /// <summary>
    ///     Tail angle for the renderer: up when angry, low when calm, and somewhere between when
    ///     tamed depending on how healthy it is.
    /// </summary>
    public float TailRotation(Entity self) =>
        IsAngry(self) ? (float)Math.PI * 0.49F
        : IsTamed(self) ? (0.55F - (_tamedHealth - ShownHealth(self)) * 0.02F) * (float)Math.PI
        : (float)Math.PI * 0.2F;
}
