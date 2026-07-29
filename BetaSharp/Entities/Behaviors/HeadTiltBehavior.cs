using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.Items.Behaviors;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Cocks the mob's head at a player holding something it wants (the taming item while wild, any
///     meat once tamed) and holds its gaze while the offer stands.
/// </summary>
public sealed class HeadTiltBehavior : IEntityPhysics, IEntityTicker
{
    private readonly int _holdGazeTicks;
    private readonly StateHandle<bool> _interested;
    private readonly StateHandle<float> _previousTilt;
    private readonly StateHandle<float> _tilt;
    private readonly float _tiltSpeed;

    private readonly Item _wantedWhileWild;

    public HeadTiltBehavior(in EntityBehaviorContext context)
    {
        _wantedWhileWild = Item.ByName(ResourceLocation.Parse(context.Json.GetProperty("wanted_while_wild").GetString()!).Path);
        _tiltSpeed = context.Float("tilt_speed", 0.4F);
        _holdGazeTicks = context.Int("hold_gaze_ticks", 10);

        _interested = context.DeclareBool();
        _tilt = context.DeclareFloat();
        _previousTilt = context.DeclareFloat();
    }

    /// <summary>Runs after movement, which is when the mob has settled on who it is looking at.</summary>
    public void AfterTickMovement(EntityLiving self)
    {
        self.State[_interested] = false;
        if (self is not EntityCreature mob || !mob.HasCurrentTarget || mob.HasPath)
        {
            return;
        }

        if (mob.Behaviors.Find<TameableBehavior>() is not { } tame || tame.IsAngry(mob))
        {
            return;
        }

        if (mob.CurrentTarget is not EntityPlayer watched)
        {
            return;
        }

        ItemStack? held = watched.Inventory.ItemInHand;
        if (held == null)
        {
            return;
        }

        self.State[_interested] = tame.IsTamed(mob)
            ? Item.ITEMS[held.ItemId]?.GetBehavior<FoodBehavior>() is { IsMeat: true }
            : held.ItemId == _wantedWhileWild.Id;
    }

    public void OnTickEnd(EntityLiving self)
    {
        EntityState state = self.State;
        state[_previousTilt] = state[_tilt];

        float target = state[_interested] ? 1.0F : 0.0F;
        state[_tilt] += (target - state[_tilt]) * _tiltSpeed;

        // Keep watching while the offer stands, instead of glancing away mid-tilt.
        if (state[_interested])
        {
            self.HoldGaze(_holdGazeTicks);
        }
    }

    /// <summary>Interpolated head angle for the renderer, which finds this behavior by capability.</summary>
    public float TiltAngle(Entity self, float tickDelta)
    {
        EntityState state = self.State;
        return (state[_previousTilt] + (state[_tilt] - state[_previousTilt]) * tickDelta) * 0.15F * (float)Math.PI;
    }
}
