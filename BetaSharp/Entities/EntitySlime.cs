using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntitySlime : EntityLiving, Monster
{
    public override EntityType Type => EntityRegistry.Slime;
    public readonly SyncedProperty<byte> SlimeSize;
    public float squishAmount;
    public float prevSquishAmount;
    private int slimeJumpDelay;

    public EntitySlime(IWorldContext world) : base(world)
    {
        texture = "/mob/slime.png";
        SlimeSize = DataSynchronizer.MakeProperty<byte>(16, 1);
        int size = 1 << Random.NextInt(3);
        StandingEyeHeight = 0.0F;
        slimeJumpDelay = Random.NextInt(20) + 10;
        setSlimeSize(size);
    }

    public void setSlimeSize(int size)
    {
        SlimeSize.Value = (byte)size;
        setBoundingBoxSpacing(0.6F * (float)size, 0.6F * (float)size);
        health = size * size;
        setPosition(X, Y, Z);
    }

    public int getSlimeSize()
    {
        return SlimeSize.Value;
    }

    public override void writeNbt(NBTTagCompound nbt)
    {
        base.writeNbt(nbt);
        nbt.SetInteger("Size", getSlimeSize() - 1);
    }

    public override void readNbt(NBTTagCompound nbt)
    {
        base.readNbt(nbt);
        setSlimeSize(nbt.GetInteger("Size") + 1);
    }

    public override void tick()
    {
        prevSquishAmount = squishAmount;
        bool wasOnGround = OnGround;
        base.tick();
        if (OnGround && !wasOnGround)
        {
            int size = getSlimeSize();

            for (int _ = 0; _ < size * 8; ++_)
            {
                float angle = Random.NextFloat() * (float)Math.PI * 2.0F;
                float spread = Random.NextFloat() * 0.5F + 0.5F;
                float offsetX = MathHelper.Sin(angle) * (float)size * 0.5F * spread;
                float offsetY = MathHelper.Cos(angle) * (float)size * 0.5F * spread;
                World.Broadcaster.AddParticle("slime", base.X + (double)offsetX, BoundingBox.MinY, Z + (double)offsetY, 0.0D, 0.0D, 0.0D);
            }

            if (size > 2)
            {
                World.Broadcaster.PlaySoundAtEntity(this, "mob.slime", getSoundVolume(), ((Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F) / 0.8F);
            }

            squishAmount = -0.5F;
        }

        squishAmount *= 0.6F;
    }

    public override void tickLiving()
    {
        func_27021_X();
        EntityPlayer player = World.Entities.GetClosestPlayerTarget(this.X, this.Y, this.Z, 16.0D);
        if (player != null)
        {
            faceEntity(player, 10.0F, 20.0F);
        }

        if (OnGround && slimeJumpDelay-- <= 0)
        {
            slimeJumpDelay = Random.NextInt(20) + 10;
            if (player != null)
            {
                slimeJumpDelay /= 3;
            }

            jumping = true;
            if (getSlimeSize() > 1)
            {
                World.Broadcaster.PlaySoundAtEntity(this, "mob.slime", getSoundVolume(), ((Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F) * 0.8F);
            }

            squishAmount = 1.0F;
            sidewaysSpeed = 1.0F - Random.NextFloat() * 2.0F;
            forwardSpeed = (float)(1 * getSlimeSize());
        }
        else
        {
            jumping = false;
            if (OnGround)
            {
                sidewaysSpeed = forwardSpeed = 0.0F;
            }
        }

    }

    public override void markDead()
    {
        int size = getSlimeSize();
        if (!World.IsRemote && size > 1 && health == 0)
        {
            for (int i = 0; i < 4; ++i)
            {
                float offsetX = ((float)(i % 2) - 0.5F) * (float)size / 4.0F;
                float offsetY = ((float)(i / 2) - 0.5F) * (float)size / 4.0F;
                EntitySlime slime = new EntitySlime(World);
                slime.setSlimeSize(size / 2);
                slime.setPositionAndAnglesKeepPrevAngles(X + (double)offsetX, Y + 0.5D, Z + (double)offsetY, Random.NextFloat() * 360.0F, 0.0F);
                World.SpawnEntity(slime);
            }
        }

        base.markDead();
    }

    public override void onPlayerInteraction(EntityPlayer player)
    {
        int size = getSlimeSize();
        if (size > 1 && canSee(player) && (double)getDistance(player) < 0.6D * (double)size && player.damage(this, size))
        {
            World.Broadcaster.PlaySoundAtEntity(this, "mob.slimeattack", 1.0F, (Random.NextFloat() - Random.NextFloat()) * 0.2F + 1.0F);
        }

    }

    protected override String getHurtSound()
    {
        return "mob.slime";
    }

    protected override String getDeathSound()
    {
        return "mob.slime";
    }

    protected override int getDropItemId()
    {
        return getSlimeSize() == 1 ? Item.Slimeball.id : 0;
    }

    public override bool canSpawn()
    {
        Chunk chunk = World.ChunkHost.GetChunkFromPos(MathHelper.Floor(X), MathHelper.Floor(Z));
        return (getSlimeSize() == 1 || World.Difficulty > 0) && Random.NextInt(10) == 0 && chunk.GetSlimeRandom(987234911L).NextInt(10) == 0 && Y < 16.0D;
    }

    protected override float getSoundVolume()
    {
        return 0.6F;
    }
}
