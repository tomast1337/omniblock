using System.Text.Json;
using BetaSharp.NBT;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Runs several behaviors from a single slot, in declared order, for a mob whose work in that
///     slot is two independent things: a ghast drifts towards a waypoint and lines up a fireball.
///     <para>
///         Children are sorted by capability once at load, so a composite in the Physics slot runs
///         only its physics children and one in the Ticker slot only its tickers. Where a hook
///         answers a question, the first child with an answer gives it; where it does work, every
///         child does its share.
///     </para>
/// </summary>
public sealed class CompositeBehavior : IEntityTicker, IEntityPhysics, IEntityLifecycle, IEntityPersistence, IEntityInteractable, IEntityTargetBehavior, IEntityAttackBehavior, IEntityBehaviorGroup
{
    private readonly IEntityAttackBehavior[] _attacks;
    private readonly object[] _children;
    private readonly IEntityInteractable[] _interactables;
    private readonly IEntityLifecycle[] _lifecycles;
    private readonly IEntityPersistence[] _persistence;
    private readonly IEntityPhysics[] _physics;
    private readonly IEntityTargetBehavior[] _targeting;
    private readonly IEntityTicker[] _tickers;

    public CompositeBehavior(in EntityBehaviorContext context)
    {
        List<object> children = [];
        foreach (JsonElement entry in context.Json.GetProperty("behaviors").EnumerateArray())
        {
            children.Add(EntityBehaviorRegistry.Build(context with
            {
                Json = entry
            }));
        }

        _children = [.. children];
        _tickers = [.. children.OfType<IEntityTicker>()];
        _physics = [.. children.OfType<IEntityPhysics>()];
        _lifecycles = [.. children.OfType<IEntityLifecycle>()];
        _persistence = [.. children.OfType<IEntityPersistence>()];
        _interactables = [.. children.OfType<IEntityInteractable>()];
        _targeting = [.. children.OfType<IEntityTargetBehavior>()];
        _attacks = [.. children.OfType<IEntityAttackBehavior>()];
    }

    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        foreach (IEntityAttackBehavior attack in _attacks)
        {
            attack.AttackEntity(self, target, distance);
        }
    }

    public void AttackBlockedEntity(EntityCreature self, Entity target, float distance)
    {
        foreach (IEntityAttackBehavior attack in _attacks)
        {
            attack.AttackBlockedEntity(self, target, distance);
        }
    }

    public IEnumerable<object> Children => _children;

    /// <summary>A right-click is consumed once, by the first child to handle it.</summary>
    public bool OnInteract(Entity self, EntityPlayer player)
    {
        foreach (IEntityInteractable interactable in _interactables)
        {
            if (interactable.OnInteract(self, player))
            {
                return true;
            }
        }

        return false;
    }

    public void OnPlayerCollision(Entity self, EntityPlayer player)
    {
        foreach (IEntityInteractable interactable in _interactables)
        {
            interactable.OnPlayerCollision(self, player);
        }
    }

    public bool OnAnimateHurt(Entity self)
    {
        bool handled = false;
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            handled |= lifecycle.OnAnimateHurt(self);
        }

        return handled;
    }

    public void OnRemoved(Entity self)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            lifecycle.OnRemoved(self);
        }
    }

    public void OnCreated(Entity self)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            lifecycle.OnCreated(self);
        }
    }

    public void OnMarkDead(EntityLiving self)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            lifecycle.OnMarkDead(self);
        }
    }

    public void OnDamaged(EntityLiving self, Entity? attacker, int amount)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            lifecycle.OnDamaged(self, attacker, amount);
        }
    }

    /// <summary>Each child adjusts what the one before it left, so resistances compound.</summary>
    public int ModifyDamage(EntityLiving self, Entity? attacker, int amount)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            amount = lifecycle.ModifyDamage(self, attacker, amount);
        }

        return amount;
    }

    public void OnDamageApplied(EntityLiving self, Entity? attacker, int amount)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            lifecycle.OnDamageApplied(self, attacker, amount);
        }
    }

    /// <summary>The first child that recognises the status byte consumes it.</summary>
    public bool OnEntityStatus(EntityLiving self, sbyte status)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            if (lifecycle.OnEntityStatus(self, status))
            {
                return true;
            }
        }

        return false;
    }

    public void OnPostSpawn(EntityLiving self)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            lifecycle.OnPostSpawn(self);
        }
    }

    public bool? Damage(Entity self, Entity? attacker, int amount) => First(_lifecycles, l => l.Damage(self, attacker, amount));

    /// <summary>The first child that handles the strike has handled it.</summary>
    public bool OnStruckByLightning(EntityLiving self, Entity bolt)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            if (lifecycle.OnStruckByLightning(self, bolt))
            {
                return true;
            }
        }

        return false;
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        foreach (IEntityPersistence persistence in _persistence)
        {
            persistence.OnWriteNbt(self, nbt);
        }
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        foreach (IEntityPersistence persistence in _persistence)
        {
            persistence.OnReadNbt(self, nbt);
        }
    }

    public bool? CanDespawn(EntityLiving self) => First(_persistence, p => p.CanDespawn(self));

    public void AfterTickMovement(EntityLiving self)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            physics.AfterTickMovement(self);
        }
    }

    public bool OnLanding(EntityLiving self, float fallDistance)
    {
        bool handled = false;
        foreach (IEntityPhysics physics in _physics)
        {
            handled |= physics.OnLanding(self, fallDistance);
        }

        return handled;
    }

    /// <summary>One move per tick: the first child that moves the mob has moved it.</summary>
    public bool Travel(EntityLiving self, float strafe, float forward)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.Travel(self, strafe, forward))
            {
                return true;
            }
        }

        return false;
    }

    public bool? CanSpawn(EntityLiving self) => First(_physics, p => p.CanSpawn(self));

    public float? GetBlockPathWeight(EntityLiving self, int x, int y, int z) => First(_physics, p => p.GetBlockPathWeight(self, x, y, z));

    public bool? IsClimbing(EntityLiving self) => First(_physics, p => p.IsClimbing(self));

    public bool? IsMovementCeased(EntityLiving self) => First(_physics, p => p.IsMovementCeased(self));

    public int? MaxFallDistance(EntityLiving self) => First(_physics, p => p.MaxFallDistance(self));

    public bool? IsInWater(Entity self) => First(_physics, p => p.IsInWater(self));

    public bool? ShouldRender(Entity self) => First(_physics, p => p.ShouldRender(self));

    public bool? CheckWaterCollisions(Entity self) => First(_physics, p => p.CheckWaterCollisions(self));

    public bool OnVelocityFromServer(Entity self, double vx, double vy, double vz)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.OnVelocityFromServer(self, vx, vy, vz))
            {
                return true;
            }
        }

        return false;
    }

    public bool OnMove(Entity self, double dx, double dy, double dz)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.OnMove(self, dx, dy, dz))
            {
                return true;
            }
        }

        return false;
    }

    public bool OnAddVelocity(Entity self, double dx, double dy, double dz)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.OnAddVelocity(self, dx, dy, dz))
            {
                return true;
            }
        }

        return false;
    }

    public bool OnPositionSync(Entity self, double x, double y, double z, float yaw, float pitch, int steps)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.OnPositionSync(self, x, y, z, yaw, pitch, steps))
            {
                return true;
            }
        }

        return false;
    }

    public bool OnUpdatePassengerPosition(Entity self)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.OnUpdatePassengerPosition(self))
            {
                return true;
            }
        }

        return false;
    }

    public bool OnCollision(Entity self, Entity other)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.OnCollision(self, other))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One target at a time: the first child that names one wins.</summary>
    public Entity? FindPlayerToAttack(EntityCreature self)
    {
        foreach (IEntityTargetBehavior targeting in _targeting)
        {
            if (targeting.FindPlayerToAttack(self) is { } target)
            {
                return target;
            }
        }

        return null;
    }

    public void OnTick(Entity self)
    {
        foreach (IEntityTicker ticker in _tickers)
        {
            ticker.OnTick(self);
        }
    }

    /// <summary>
    ///     Every child ticks, and the base tick is skipped if any of them owns the whole tick, the
    ///     same rule <see cref="OnTickLiving" /> applies to the AI.
    /// </summary>
    public bool OnTickEntity(Entity self)
    {
        bool replacesTick = false;
        foreach (IEntityTicker ticker in _tickers)
        {
            replacesTick |= ticker.OnTickEntity(self);
        }

        return replacesTick;
    }

    public void OnTickMovement(EntityLiving self)
    {
        foreach (IEntityTicker ticker in _tickers)
        {
            ticker.OnTickMovement(self);
        }
    }

    /// <summary>
    ///     Every child ticks, and the mob counts as having its own AI if any of them says so. One
    ///     child declaring itself the AI does not silence the rest.
    /// </summary>
    public bool OnTickLiving(EntityLiving self)
    {
        bool replacesAi = false;
        foreach (IEntityTicker ticker in _tickers)
        {
            replacesAi |= ticker.OnTickLiving(self);
        }

        return replacesAi;
    }

    public void AfterTickLiving(EntityLiving self)
    {
        foreach (IEntityTicker ticker in _tickers)
        {
            ticker.AfterTickLiving(self);
        }
    }

    public void OnTickEnd(EntityLiving self)
    {
        foreach (IEntityTicker ticker in _tickers)
        {
            ticker.OnTickEnd(self);
        }
    }

    /// <summary>One sound at a time: the first child with something to say says it.</summary>
    public string? LivingSound(EntityLiving self)
    {
        foreach (IEntityTicker ticker in _tickers)
        {
            if (ticker.LivingSound(self) is { } sound)
            {
                return sound;
            }
        }

        return null;
    }

    /// <summary>The first child with an opinion answers; the rest are not consulted.</summary>
    private static TAnswer? First<TChild, TAnswer>(TChild[] children, Func<TChild, TAnswer?> ask) where TAnswer : struct
    {
        foreach (TChild child in children)
        {
            if (ask(child) is { } answer)
            {
                return answer;
            }
        }

        return null;
    }
}
