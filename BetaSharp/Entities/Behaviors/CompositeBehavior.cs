using System.Text.Json;
using BetaSharp.NBT;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Runs several behaviors from a single slot, in declared order. A mob whose per-tick work is
///     genuinely two independent things — a ghast drifts towards a waypoint <em>and</em> lines up a
///     fireball — would otherwise have to fuse them into one behavior just to fit one slot.
///     <para>
///         Children are sorted by capability once at load, so a composite in the Physics slot runs
///         only the children that are physics and a composite in the Ticker slot only the tickers.
///         Where a hook answers a question rather than doing work, the first child with an answer
///         gives it; where it does work, every child does its share.
///     </para>
/// </summary>
public sealed class CompositeBehavior : IEntityTicker, IEntityPhysics, IEntityLifecycle, IEntityPersistence, IEntityBehaviorGroup
{
    private readonly object[] _children;
    private readonly IEntityTicker[] _tickers;
    private readonly IEntityPhysics[] _physics;
    private readonly IEntityLifecycle[] _lifecycles;
    private readonly IEntityPersistence[] _persistence;

    public CompositeBehavior(in EntityBehaviorContext context)
    {
        List<object> children = [];
        foreach (JsonElement entry in context.Json.GetProperty("behaviors").EnumerateArray())
        {
            children.Add(EntityBehaviorRegistry.Build(context with { Json = entry }));
        }

        _children = [.. children];
        _tickers = [.. children.OfType<IEntityTicker>()];
        _physics = [.. children.OfType<IEntityPhysics>()];
        _lifecycles = [.. children.OfType<IEntityLifecycle>()];
        _persistence = [.. children.OfType<IEntityPersistence>()];
    }

    public IEnumerable<object> Children => _children;

    public void OnTick(Entity self)
    {
        foreach (IEntityTicker ticker in _tickers) ticker.OnTick(self);
    }

    public void OnTickMovement(EntityLiving self)
    {
        foreach (IEntityTicker ticker in _tickers) ticker.OnTickMovement(self);
    }

    /// <summary>
    ///     Every child ticks, and the mob counts as having its own AI if any of them says so — one
    ///     child declaring itself the AI does not silence the rest.
    /// </summary>
    public bool OnTickLiving(EntityLiving self)
    {
        bool replacesAi = false;
        foreach (IEntityTicker ticker in _tickers) replacesAi |= ticker.OnTickLiving(self);
        return replacesAi;
    }

    public void OnTickEnd(EntityLiving self)
    {
        foreach (IEntityTicker ticker in _tickers) ticker.OnTickEnd(self);
    }

    public void AfterTickMovement(EntityLiving self)
    {
        foreach (IEntityPhysics physics in _physics) physics.AfterTickMovement(self);
    }

    public bool OnLanding(EntityLiving self, float fallDistance)
    {
        bool handled = false;
        foreach (IEntityPhysics physics in _physics) handled |= physics.OnLanding(self, fallDistance);
        return handled;
    }

    /// <summary>Movement is singular: the first child that moves the mob has moved it.</summary>
    public bool Travel(EntityLiving self, float strafe, float forward)
    {
        foreach (IEntityPhysics physics in _physics)
        {
            if (physics.Travel(self, strafe, forward)) return true;
        }

        return false;
    }

    public bool? CanSpawn(EntityLiving self) => First(_physics, p => p.CanSpawn(self));

    public float? GetBlockPathWeight(EntityLiving self, int x, int y, int z) => First(_physics, p => p.GetBlockPathWeight(self, x, y, z));

    public bool? IsClimbing(EntityLiving self) => First(_physics, p => p.IsClimbing(self));

    public bool? IsInWater(Entity self) => First(_physics, p => p.IsInWater(self));

    public void OnCreated(EntityLiving self)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles) lifecycle.OnCreated(self);
    }

    public void OnMarkDead(EntityLiving self)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles) lifecycle.OnMarkDead(self);
    }

    public void OnDamaged(EntityLiving self, Entity? attacker, int amount)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles) lifecycle.OnDamaged(self, attacker, amount);
    }

    public void OnPostSpawn(EntityLiving self)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles) lifecycle.OnPostSpawn(self);
    }

    /// <summary>A strike is one event: the first child that handles it has handled it.</summary>
    public bool OnStruckByLightning(EntityLiving self, EntityLightningBolt bolt)
    {
        foreach (IEntityLifecycle lifecycle in _lifecycles)
        {
            if (lifecycle.OnStruckByLightning(self, bolt)) return true;
        }

        return false;
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        foreach (IEntityPersistence persistence in _persistence) persistence.OnWriteNbt(self, nbt);
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        foreach (IEntityPersistence persistence in _persistence) persistence.OnReadNbt(self, nbt);
    }

    /// <summary>The first child with an opinion answers; the rest are not consulted.</summary>
    private static T? First<T>(IEntityPhysics[] children, Func<IEntityPhysics, T?> ask) where T : struct
    {
        foreach (IEntityPhysics child in children)
        {
            if (ask(child) is { } answer) return answer;
        }

        return null;
    }
}
