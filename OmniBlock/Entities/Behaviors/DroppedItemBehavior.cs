using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities.State;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     An item lying in the world. Tumbles with block friction, floats up out of lava, waits out its
///     pickup delay, and vanishes into whoever walks over it, or into nothing after five minutes.
///     One behavior across five slots, because every hook reads the same stack and the same small
///     pool of hit points.
///     <para>
///         Which stack is dropped is per-instance state set by whoever spawned it, the same shape as
///         falling sand's carried block. <see cref="Create" /> is the one spawner.
///     </para>
/// </summary>
public sealed class DroppedItemBehavior : IEntityTicker, IEntityLifecycle, IEntityPersistence, IEntityInteractable, IEntityPhysics
{
    private readonly StateHandle<int> _age;
    private readonly StateHandle<float> _bobPhase;

    private readonly int _despawnAge;
    private readonly StateHandle<int> _health;

    /// <summary>Item id to the achievement its pickup awards, declared in the definition.</summary>
    private readonly (int ItemId, Achievement Achievement)[] _pickupAchievements;

    private readonly StateHandle<int> _pickupDelay;
    private readonly StateHandle<ItemStack> _stack;

    public DroppedItemBehavior(EntityStateLayout layout, int despawnAge, int health, (int ItemId, Achievement Achievement)[] pickupAchievements)
    {
        _despawnAge = despawnAge;
        _pickupAchievements = pickupAchievements;

        _stack = layout.DeclareRef<ItemStack>();
        _health = layout.DeclareInt(health);
        _age = layout.DeclareInt();
        _pickupDelay = layout.DeclareInt();
        _bobPhase = layout.DeclareFloat();
    }

    public void OnPlayerCollision(Entity self, EntityPlayer player)
    {
        if (self.World.IsRemote || !player.GameMode.CanPickup)
        {
            return;
        }

        if (Stack(self) is not { } stack)
        {
            return;
        }

        if (self.State[_pickupDelay] != 0 || !player.Inventory.AddItemStackToInventory(stack))
        {
            return;
        }

        foreach ((int itemId, Achievement achievement) in _pickupAchievements)
        {
            if (stack.ItemId == itemId)
            {
                player.IncrementStat(achievement);
            }
        }

        self.World.Broadcaster.PlaySoundAtEntity(self, "random.pop", 0.2F, ((self.Random.NextFloat() - self.Random.NextFloat()) * 0.7F + 1.0F) * 2.0F);
        player.sendPickup(self, stack.Count);
        if (stack.Count <= 0)
        {
            self.MarkDead();
        }
    }

    public void OnCreated(Entity self)
    {
        self.State[_bobPhase] = Random.Shared.NextSingle() * (float)Math.PI * 2.0F;
        self.Yaw = Random.Shared.NextSingle() * 360.0F;
        self.VelocityX = Random.Shared.NextDouble() * 0.2F - 0.1F;
        self.VelocityY = 0.2F;
        self.VelocityZ = Random.Shared.NextDouble() * 0.2F - 0.1F;
    }

    /// <summary>Fire and explosions spend hit points; the hit never registers as landed.</summary>
    public bool? Damage(Entity self, Entity? attacker, int amount)
    {
        self.VelocityModified = true;
        self.State[_health] -= amount;
        if (self.State[_health] <= 0)
        {
            self.MarkDead();
        }

        return false;
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        nbt.SetShort("Health", (byte)self.State[_health]);
        nbt.SetShort("Age", (short)self.State[_age]);
        if (Stack(self) is { } stack)
        {
            nbt.SetCompoundTag("Item", stack.WriteToNbt(new NBTTagCompound()));
        }
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        self.State[_health] = nbt.GetShort("Health") & 255;
        self.State[_age] = nbt.GetShort("Age");
        SetStack(self, new ItemStack(nbt.GetCompoundTag("Item")));
    }

    /// <summary>The item's whole box probes for water, and the current carries it in the same pass.</summary>
    public bool? CheckWaterCollisions(Entity self) =>
        self.World.Reader.UpdateMovementInFluid(self.BoundingBox, Material.Water, self);

    public bool OnTickEntity(Entity self)
    {
        self.BaseTick();

        if (self.State[_pickupDelay] > 0)
        {
            --self.State[_pickupDelay];
        }

        self.PrevX = self.X;
        self.PrevY = self.Y;
        self.PrevZ = self.Z;
        self.VelocityY -= 0.04F;
        if (self.World.Reader.GetMaterial(MathHelper.Floor(self.X), MathHelper.Floor(self.Y), MathHelper.Floor(self.Z)) == Material.Lava)
        {
            self.VelocityY = 0.2F;
            self.VelocityX = (self.Random.NextFloat() - self.Random.NextFloat()) * 0.2F;
            self.VelocityZ = (self.Random.NextFloat() - self.Random.NextFloat()) * 0.2F;
            self.World.Broadcaster.PlaySoundAtEntity(self, "random.fizz", 0.4F, 2.0F + self.Random.NextFloat() * 0.4F);
        }

        self.PushOutOfBlocks(self.X, (self.BoundingBox.MinY + self.BoundingBox.MaxY) / 2.0D, self.Z);
        self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
        float friction = 0.98F;
        if (self.OnGround)
        {
            friction = 0.1F * 0.1F * 58.8F;
            int groundBlockId = self.World.Reader.GetBlockId(MathHelper.Floor(self.X), MathHelper.Floor(self.BoundingBox.MinY) - 1, MathHelper.Floor(self.Z));
            if (groundBlockId > 0)
            {
                friction = Block.Blocks[groundBlockId].Slipperiness * 0.98F;
            }
        }

        self.VelocityX *= friction;
        self.VelocityY *= 0.98F;
        self.VelocityZ *= friction;
        if (self.OnGround)
        {
            self.VelocityY *= -0.5D;
        }

        if (++self.State[_age] >= _despawnAge)
        {
            self.MarkDead();
        }

        return true;
    }

    /// <summary>
    ///     Creates a dropped item, gives it its stack and pickup delay, and positions it. The caller
    ///     spawns it, and may nudge its velocity first.
    /// </summary>
    public static Entity Create(IWorldContext world, double x, double y, double z, ItemStack stack, int pickupDelay = 0)
    {
        Entity item = EntityRegistry.ByName("item").Create(world);
        DroppedItemBehavior dropped = item.Behaviors.Find<DroppedItemBehavior>()!;
        dropped.SetStack(item, stack);
        dropped.SetPickupDelay(item, pickupDelay);
        item.SetPositionAndAngles(x, y, z, item.Yaw, 0.0F);
        return item;
    }

    public ItemStack? Stack(Entity self) => self.State.GetRef(_stack);

    public void SetStack(Entity self, ItemStack stack) => self.State.SetRef(_stack, stack);

    public int PickupDelay(Entity self) => self.State[_pickupDelay];

    public void SetPickupDelay(Entity self, int delay) => self.State[_pickupDelay] = delay;

    /// <summary>Phase offset for the renderer's bob and spin, so a pile of drops does not move in lockstep.</summary>
    public float BobPhase(Entity self) => self.State[_bobPhase];

    public int ItemAge(Entity self) => self.State[_age];
}
