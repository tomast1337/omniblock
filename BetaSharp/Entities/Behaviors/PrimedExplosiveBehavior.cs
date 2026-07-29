using BetaSharp.Entities.State;
using BetaSharp.NBT;
using BetaSharp.Rules;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A lit block of explosive: created armed with a kick of velocity, it tumbles under gravity,
///     counts its fuse down and detonates. One behavior across the Ticker, Lifecycle and Persistence
///     slots because every hook moves the same fuse.
///     <para>
///         The tick replaces the base tick entirely (via <see cref="IEntityTicker.OnTickEntity" />)
///         — a primed block does not age, burn, or die in the void, matching the override this
///         replaced.
///     </para>
/// </summary>
public sealed class PrimedExplosiveBehavior : IEntityTicker, IEntityLifecycle, IEntityPersistence
{
    private readonly StateHandle<int> _fuse;

    private readonly int _fuseTicks;
    private readonly float _power;
    private readonly string _particle;

    public PrimedExplosiveBehavior(EntityStateLayout layout, int fuseTicks, float power, string particle)
    {
        _fuseTicks = fuseTicks;
        _power = power;
        _particle = particle;

        _fuse = layout.DeclareInt();
    }

    public int FuseTicks(Entity self) => self.State[_fuse];

    public void SetFuse(Entity self, int ticks) => self.State[_fuse] = ticks;

    /// <summary>
    ///     A block set off by a nearby explosion gets a short, randomised fuse rather than the full
    ///     one — the chain reaction is fast.
    /// </summary>
    public void ShortenFuse(Entity self) =>
        self.State[_fuse] = self.World.Random.NextInt(_fuseTicks / 4) + _fuseTicks / 8;

    /// <summary>
    ///     Armed the moment it exists: full fuse, a small random horizontal kick and a pop upward.
    ///     The kick's angle expression is kept verbatim from the class it replaced, quirks included.
    /// </summary>
    public void OnCreated(Entity self)
    {
        self.State[_fuse] = _fuseTicks;
        float randomAngle = (float)(System.Random.Shared.NextSingle() * Math.PI * 2.0D);
        self.VelocityX = -MathHelper.Sin(randomAngle * (float)Math.PI / 180.0F) * 0.02F;
        self.VelocityY = 0.2F;
        self.VelocityZ = -MathHelper.Cos(randomAngle * (float)Math.PI / 180.0F) * 0.02F;
    }

    public bool OnTickEntity(Entity self)
    {
        self.PrevX = self.X;
        self.PrevY = self.Y;
        self.PrevZ = self.Z;
        self.VelocityY -= 0.04F;
        self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
        self.VelocityX *= 0.98F;
        self.VelocityY *= 0.98F;
        self.VelocityZ *= 0.98F;
        if (self.OnGround)
        {
            self.VelocityX *= 0.7F;
            self.VelocityZ *= 0.7F;
            self.VelocityY *= -0.5D;
        }

        if (self.State[_fuse]-- <= 0)
        {
            self.MarkDead();
            if (!self.World.IsRemote) Explode(self);
        }
        else
        {
            self.World.Broadcaster.AddParticle(_particle, self.X, self.Y + 0.5D, self.Z, 0.0D, 0.0D, 0.0D);
        }

        return true;
    }

    private void Explode(Entity self)
    {
        if (!self.World.Rules.GetBool(DefaultRules.TntExplodes)) return;

        self.World.CreateExplosion(null, self.X, self.Y, self.Z, _power);
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt) => nbt.SetByte("Fuse", (sbyte)self.State[_fuse]);

    public void OnReadNbt(Entity self, NBTTagCompound nbt) => self.State[_fuse] = nbt.GetByte("Fuse");
}
