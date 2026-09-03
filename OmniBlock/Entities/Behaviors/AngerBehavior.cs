using System.Text.Json;
using OmniBlock.Entities.State;
using OmniBlock.NBT;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Neutral-until-provoked aggression: the mob ignores players until one hits it, then every one
///     of its kind nearby turns on the attacker and stays angry for a while.
///     <para>
///         Four slots from one entry, because all four read the same anger timer: Targeting gates on
///         it, Ticker counts it down and grunts, Lifecycle sets it, Persistence saves it.
///     </para>
/// </summary>
public sealed class AngerBehavior : IEntityTicker, IEntityTargetBehavior, IEntityLifecycle, IEntityPersistence
{
    private readonly double _alertRadius;
    private readonly StateHandle<int> _anger;
    private readonly int _angerRange;
    private readonly string _angrySound;
    private readonly float _angrySpeed;
    private readonly float _calmSpeed;
    private readonly int _minAnger;
    private readonly StateHandle<int> _soundDelay;
    private readonly int _soundDelayRange;

    private readonly IEntityTargetBehavior _whenAngry;

    public AngerBehavior(in EntityBehaviorContext context)
    {
        _minAnger = context.Int("min_anger", 400);
        _angerRange = context.Int("anger_range", 400);
        _soundDelayRange = context.Int("sound_delay_range", 40);
        _alertRadius = context.Double("alert_radius", 32.0D);
        _calmSpeed = context.Float("calm_speed", 0.5F);
        _angrySpeed = context.Float("angry_speed", 0.95F);
        _angrySound = context.Json.TryGetProperty("angry_sound", out JsonElement s)
            ? s.GetString() ?? ""
            : "";

        _anger = context.DeclareInt();
        _soundDelay = context.DeclareInt();

        // Whom to hunt once angered is a separate behavior; the anger only gates it.
        _whenAngry = context.Json.TryGetProperty("when_angry", out JsonElement angry)
            ? (IEntityTargetBehavior)context.Build(angry)
            : new AlwaysHuntTargetBehavior();
    }

    /// <summary>
    ///     Being hit by a targetable player angers this mob and every one of its kind nearby.
    /// </summary>
    public void OnDamaged(EntityLiving self, Entity? attacker, int amount)
    {
        if (attacker is not EntityPlayer { GameMode.CanBeTargeted: true })
        {
            return;
        }

        foreach (Entity nearby in self.World.Entities.GetEntities(self, self.BoundingBox.Expand(_alertRadius, _alertRadius, _alertRadius)))
        {
            // Same behavior instance means same entity type, so only its own kind joins in.
            if (ReferenceEquals(nearby.Behaviors.Find<AngerBehavior>(), this))
            {
                Provoke(nearby, attacker);
            }
        }

        Provoke(self, attacker);
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt) => nbt.SetShort("Anger", (short)self.State[_anger]);

    public void OnReadNbt(Entity self, NBTTagCompound nbt) => self.State[_anger] = nbt.GetShort("Anger");

    /// <summary>Only hunts while provoked; unprovoked it has no target at all.</summary>
    public Entity? FindPlayerToAttack(EntityCreature self) =>
        IsAngry(self) ? _whenAngry.FindPlayerToAttack(self) : null;

    public void OnTick(Entity self)
    {
        if (self is not EntityCreature creature)
        {
            return;
        }

        creature.MovementSpeed = creature.Target != null ? _angrySpeed : _calmSpeed;

        int delay = self.State[_soundDelay];
        if (delay <= 0 || --self.State[_soundDelay] != 0 || _angrySound.Length == 0)
        {
            return;
        }

        self.World.Broadcaster.PlaySoundAtEntity(
            self,
            _angrySound,
            creature.SoundVolume * 2.0F,
            ((self.Random.NextFloat() - self.Random.NextFloat()) * 0.2F + 1.0F) * 1.8F);
    }

    public bool IsAngry(Entity self) => self.State[_anger] > 0;

    private void Provoke(Entity self, Entity attacker)
    {
        if (self is EntityCreature creature)
        {
            creature.Target = attacker;
        }

        self.State[_anger] = _minAnger + self.Random.NextInt(_angerRange);
        self.State[_soundDelay] = self.Random.NextInt(_soundDelayRange);
    }
}
