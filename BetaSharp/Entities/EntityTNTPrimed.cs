using BetaSharp.NBT;
using BetaSharp.Rules;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityTNTPrimed : Entity
{
    public override EntityType Type => EntityRegistry.PrimedTnt;
    public int fuse;

    public EntityTNTPrimed(IWorldContext world) : base(world)
    {
        fuse = 0;
        PreventEntitySpawning = true;
        setBoundingBoxSpacing(0.98F, 0.98F);
        StandingEyeHeight = Height / 2.0F;
    }

    public EntityTNTPrimed(IWorldContext world, double x, double y, double z) : base(world)
    {
        setPosition(x, y, z);
        float randomAngle = (float)(System.Random.Shared.NextSingle() * (Math.PI) * 2.0D);
        VelocityX = (double)(-MathHelper.Sin(randomAngle * (float)Math.PI / 180.0F) * 0.02F);
        VelocityY = (double)0.2F;
        VelocityZ = (double)(-MathHelper.Cos(randomAngle * (float)Math.PI / 180.0F) * 0.02F);
        fuse = 80;
        PrevX = x;
        PrevY = y;
        PrevZ = z;
    }


    protected override bool bypassesSteppingEffects()
    {
        return false;
    }

    public override bool isCollidable()
    {
        return !Dead;
    }

    public override void tick()
    {
        PrevX = X;
        PrevY = Y;
        PrevZ = Z;
        VelocityY -= (double)0.04F;
        move(VelocityX, VelocityY, VelocityZ);
        VelocityX *= (double)0.98F;
        VelocityY *= (double)0.98F;
        VelocityZ *= (double)0.98F;
        if (OnGround)
        {
            VelocityX *= (double)0.7F;
            VelocityZ *= (double)0.7F;
            VelocityY *= -0.5D;
        }

        if (fuse-- <= 0)
        {
            if (!World.IsRemote)
            {
                markDead();
                explode();
            }
            else
            {
                markDead();
            }
        }
        else
        {
            World.Broadcaster.AddParticle("smoke", X, Y + 0.5D, Z, 0.0D, 0.0D, 0.0D);
        }

    }

    private void explode()
    {
        if (!World.Rules.GetBool(DefaultRules.TntExplodes))
        {
            return;
        }

        const float power = 4.0F;
        World.CreateExplosion((Entity)null, X, Y, Z, power);
    }

    public override void writeNbt(NBTTagCompound nbt)
    {
        nbt.SetByte("Fuse", (sbyte)fuse);
    }

    public override void readNbt(NBTTagCompound nbt)
    {
        fuse = nbt.GetByte("Fuse");
    }

    public override float getShadowRadius()
    {
        return 0.0F;
    }
}
