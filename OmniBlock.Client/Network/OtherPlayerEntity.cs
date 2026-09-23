using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Network;

public class OtherPlayerEntity : EntityPlayer
{
    private double lerpPitch;
    private int lerpSteps;
    private double lerpX;
    private double lerpY;
    private double lerpYaw;
    private double lerpZ;

    public OtherPlayerEntity(World world, string name) : base(world)
    {
        Name = name;
        StandingEyeHeight = 0.0F;
        StepHeight = 0.0F;
        NoClip = true;
        SleepOffsetY = 0.25F;
        RenderDistanceWeight = 10.0D;
    }

    protected override void resetEyeHeight() => StandingEyeHeight = 0.0F;

    public override bool Damage(Entity? ent, int amount) => true;

    public override void SetPositionAndAnglesAvoidEntities(double lerpX, double lerpY, double lerpZ, float lerpYaw, float lerpPitch, int lerpSteps)
    {
        this.lerpX = lerpX;
        this.lerpY = lerpY;
        this.lerpZ = lerpZ;
        this.lerpYaw = lerpYaw;
        this.lerpPitch = lerpPitch;
        this.lerpSteps = lerpSteps;
    }

    public override void Tick()
    {
        SleepOffsetY = 0.0F;
        base.Tick();
        LastWalkAnimationSpeed = WalkAnimationSpeed;
        var dx = X - PrevX;
        var dz = Z - PrevZ;
        var horizontalDistance = MathHelper.Sqrt(dx * dx + dz * dz) * 4.0F;
        if (horizontalDistance > 1.0F)
        {
            horizontalDistance = 1.0F;
        }

        WalkAnimationSpeed += (horizontalDistance - WalkAnimationSpeed) * 0.4F;
        AnimationPhase += WalkAnimationSpeed;
    }

    public override float GetShadowRadius() => 0.0F;

    protected override void TickMovement()
    {
        base.TickLiving();
        if (lerpSteps > 0)
        {
            var newX = X + (lerpX - X) / lerpSteps;
            var newY = Y + (lerpY - Y) / lerpSteps;
            var newZ = Z + (lerpZ - Z) / lerpSteps;

            double dYaw;
            for (dYaw = lerpYaw - Yaw; dYaw < -180.0D; dYaw += 360.0D)
            {
            }

            while (dYaw >= 180.0D)
            {
                dYaw -= 360.0D;
            }

            Yaw = (float)(Yaw + dYaw / lerpSteps);
            Pitch = (float)(Pitch + (lerpPitch - Pitch) / lerpSteps);
            --lerpSteps;
            SetPosition(newX, newY, newZ);
            SetRotation(Yaw, Pitch);
        }

        PrevStepBobbingAmount = StepBobbingAmount;
        var horizontalSpeed = MathHelper.Sqrt(VelocityX * VelocityX + VelocityZ * VelocityZ);
        var tiltAmount = (float)Math.Atan(-VelocityY * 0.2F) * 15.0F;
        if (horizontalSpeed > 0.1F)
        {
            horizontalSpeed = 0.1F;
        }

        if (!OnGround || Health <= 0)
        {
            horizontalSpeed = 0.0F;
        }

        if (OnGround || Health <= 0)
        {
            tiltAmount = 0.0F;
        }

        StepBobbingAmount += (horizontalSpeed - StepBobbingAmount) * 0.4F;
        Tilt += (tiltAmount - Tilt) * 0.8F;
    }

    public override void SetEquipmentStack(int slotIndex, int itemId, int damage)
    {
        ItemStack? itemStack = null;
        if (itemId >= 0)
        {
            itemStack = new ItemStack(World.Content.Items, itemId, 1, damage);
        }

        if (slotIndex == 0)
        {
            Inventory.Main[Inventory.SelectedSlot] = itemStack;
        }
        else
        {
            Inventory.Armor[slotIndex - 1] = itemStack;
        }
    }

    public override void Spawn()
    {
    }
}
