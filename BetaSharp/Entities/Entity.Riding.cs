namespace BetaSharp.Entities;

/// <summary>
///     Riding: mounting, dismounting, and carrying a passenger along. A vehicle drives its
///     passenger's position every tick, so the two halves of the relationship are kept together.
/// </summary>
public abstract partial class Entity
{
    private double _vehiclePitchDelta;
    private double _vehicleYawDelta;

    public virtual void TickRiding()
    {
        if (Vehicle is { Dead: true })
        {
            Vehicle = null;
            return;
        }

        VelocityX = 0.0D;
        VelocityY = 0.0D;
        VelocityZ = 0.0D;
        Tick();
        if (Vehicle == null)
        {
            return;
        }

        Vehicle.UpdatePassengerPosition();
        _vehicleYawDelta += Vehicle.Yaw - Vehicle.PrevYaw;

        _vehiclePitchDelta += Vehicle.Pitch - Vehicle.PrevPitch;

        while (_vehicleYawDelta >= 180.0D)
        {
            _vehicleYawDelta -= 360.0D;
        }

        while (_vehicleYawDelta < -180.0D)
        {
            _vehicleYawDelta += 360.0D;
        }

        while (_vehiclePitchDelta >= 180.0D)
        {
            _vehiclePitchDelta -= 360.0D;
        }

        while (_vehiclePitchDelta < -180.0D)
        {
            _vehiclePitchDelta += 360.0D;
        }

        double yawDelta = _vehicleYawDelta * 0.5D;
        double pitchDelta = _vehiclePitchDelta * 0.5D;
        const double limit = 10.0F;
        if (yawDelta > limit)
        {
            yawDelta = limit;
        }

        if (yawDelta < -limit)
        {
            yawDelta = -limit;
        }

        if (pitchDelta < -limit)
        {
            pitchDelta = -limit;
        }

        _vehicleYawDelta -= yawDelta;
        _vehiclePitchDelta -= pitchDelta;
        Yaw = (float)(Yaw + yawDelta);
        Pitch = (float)(Pitch + pitchDelta);
    }

    public virtual void UpdatePassengerPosition() => Passenger?.SetPosition(X, Y + PassengerRidingHeight + Passenger.StandingEyeHeight, Z);

    public virtual void SetVehicle(Entity? entity)
    {
        _vehiclePitchDelta = 0.0D;
        _vehicleYawDelta = 0.0D;
        if (entity == null)
        {
            if (Vehicle != null)
            {
                SetPositionAndAnglesKeepPrevAngles(Vehicle.X, Vehicle.BoundingBox.MinY + Vehicle.Height, Vehicle.Z, Yaw, Pitch);
                Vehicle.Passenger = null;
            }

            Vehicle = null;
        }
        else if (Equals(Vehicle, entity))
        {
            Vehicle.Passenger = null;
            Vehicle = null;
            SetPositionAndAnglesKeepPrevAngles(entity.X, entity.BoundingBox.MinY + entity.Height, entity.Z, Yaw, Pitch);
        }
        else
        {
            Vehicle?.Passenger = null;
            entity.Passenger?.Vehicle = null;
            Vehicle = entity;
            entity.Passenger = this;
        }
    }
}
