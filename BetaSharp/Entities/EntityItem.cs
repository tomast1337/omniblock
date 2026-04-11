using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityItem : Entity
{
    public override EntityType Type => EntityRegistry.Item;
    public ItemStack stack;
    public int itemAge;
    public int delayBeforeCanPickup;
    private int health = 5;
    public float bobPhase = System.Random.Shared.NextSingle() * ((float)Math.PI) * 2.0f;

    public EntityItem(IWorldContext world, double x, double y, double z, ItemStack stack) : base(world)
    {
        setBoundingBoxSpacing(0.25F, 0.25F);
        StandingEyeHeight = Height / 2.0F;
        setPosition(x, y, z);
        this.stack = stack;
        Yaw = System.Random.Shared.NextSingle() * 360.0f;
        VelocityX = System.Random.Shared.NextDouble() * 0.2f - 0.1f;
        VelocityY = 0.2F;
        VelocityZ = System.Random.Shared.NextDouble() * 0.2f - 0.1f;
    }

    protected override bool bypassesSteppingEffects()
    {
        return false;
    }

    public EntityItem(IWorldContext world) : base(world)
    {
        setBoundingBoxSpacing(0.25F, 0.25F);
        StandingEyeHeight = Height / 2.0F;
    }


    public override void tick()
    {
        base.tick();
        if (delayBeforeCanPickup > 0)
        {
            --delayBeforeCanPickup;
        }

        PrevX = X;
        PrevY = Y;
        PrevZ = Z;
        VelocityY -= (double)0.04F;
        if (World.Reader.GetMaterial(MathHelper.Floor(X), MathHelper.Floor(Y), MathHelper.Floor(Z)) == Material.Lava)
        {
            VelocityY = (double)0.2F;
            VelocityX = (double)((Random.NextFloat() - Random.NextFloat()) * 0.2F);
            VelocityZ = (double)((Random.NextFloat() - Random.NextFloat()) * 0.2F);
            World.Broadcaster.PlaySoundAtEntity(this, "random.fizz", 0.4F, 2.0F + Random.NextFloat() * 0.4F);
        }

        pushOutOfBlocks(X, (BoundingBox.MinY + BoundingBox.MaxY) / 2.0D, Z);
        move(VelocityX, VelocityY, VelocityZ);
        float friction = 0.98F;
        if (OnGround)
        {
            friction = 0.1F * 0.1F * 58.8F;
            int groundBlockId = World.Reader.GetBlockId(MathHelper.Floor(X), MathHelper.Floor(BoundingBox.MinY) - 1, MathHelper.Floor(Z));
            if (groundBlockId > 0)
            {
                friction = Block.Blocks[groundBlockId].Slipperiness * 0.98F;
            }
        }

        VelocityX *= (double)friction;
        VelocityY *= (double)0.98F;
        VelocityZ *= (double)friction;
        if (OnGround)
        {
            VelocityY *= -0.5D;
        }

        ++itemAge;
        if (itemAge >= 6000)
        {
            markDead();
        }

    }

    public override bool checkWaterCollisions()
    {
        return World.Reader.UpdateMovementInFluid(BoundingBox, Material.Water, this);
    }

    protected override void damage(int amount)
    {
        damage((Entity)null, amount);
    }

    public override bool damage(Entity entity, int amount)
    {
        scheduleVelocityUpdate();
        health -= amount;
        if (health <= 0)
        {
            markDead();
        }

        return false;
    }

    public override void writeNbt(NBTTagCompound nbt)
    {
        nbt.SetShort("Health", (short)((byte)health));
        nbt.SetShort("Age", (short)itemAge);
        nbt.SetCompoundTag("Item", stack.writeToNBT(new NBTTagCompound()));
    }

    public override void readNbt(NBTTagCompound nbt)
    {
        health = nbt.GetShort("Health") & 255;
        itemAge = nbt.GetShort("Age");
        NBTTagCompound itemTag = nbt.GetCompoundTag("Item");
        stack = new ItemStack(itemTag);
    }

    public override void onPlayerInteraction(EntityPlayer player)
    {
        if (!World.IsRemote && player.GameMode.CanPickup)
        {
            int pickedUpCount = stack.Count;
            if (delayBeforeCanPickup == 0 && player.inventory.AddItemStackToInventory(stack))
            {
                if (stack.ItemId == Block.Log.id)
                {
                    player.incrementStat(Achievements.MineWood);
                }

                if (stack.ItemId == Item.Leather.id)
                {
                    player.incrementStat(Achievements.KillCow);
                }

                World.Broadcaster.PlaySoundAtEntity(this, "random.pop", 0.2F, ((Random.NextFloat() - Random.NextFloat()) * 0.7F + 1.0F) * 2.0F);
                player.sendPickup(this, pickedUpCount);
                if (stack.Count <= 0)
                {
                    markDead();
                }
            }

        }
    }
}
