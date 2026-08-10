using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Util.Maths;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Shared static helpers for blocks whose tile entity implements <see cref="IInventory" />:
///     scatters inventory contents on break and creates the entity on placement. The public
///     <see cref="IgnoreBlockRemoval" /> flag is used by <c>FurnaceBehavior.UpdateLitState</c> to
///     suppress drops during the lit/unlit id swap.
/// </summary>
public static class InventoryUtility
{
    private const float DropSpread = 0.05F;
    public static readonly ThreadLocal<bool> IgnoreBlockRemoval = new(() => false);

    private static readonly ThreadLocal<JavaRandom> s_random = new(() => new JavaRandom());

    public static void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (block.GetBlockEntity() is { } blockEntity)
        {
            @event.World.Entities.SetBlockEntity(@event.X, @event.Y, @event.Z, blockEntity);
        }
    }

    public static void OnBreak(Block block, OnBreakEvent @event)
    {
        if (IgnoreBlockRemoval.Value) return;

        BlockEntity? entity = @event.World.Entities.GetBlockEntity<BlockEntity>(@event.X, @event.Y, @event.Z);
        if (entity is not IInventory inventory)
        {
            if (entity != null)
            {
                @event.World.Entities.RemoveBlockEntity(@event.X, @event.Y, @event.Z);
            }

            return;
        }

        JavaRandom random = s_random.Value!;

        for (int slot = 0; slot < inventory.Size; ++slot)
        {
            ItemStack? stack = inventory.GetStack(slot);
            if (stack == null) continue;

            float offsetX = random.NextFloat() * 0.8F + 0.1F;
            float offsetY = random.NextFloat() * 0.8F + 0.1F;
            float offsetZ = random.NextFloat() * 0.8F + 0.1F;

            while (stack.Count > 0)
            {
                int amount = random.NextInt(21) + 10;
                if (amount > stack.Count)
                {
                    amount = stack.Count;
                }

                stack.Count -= amount;
                Entity entityItem = DroppedItemBehavior.Create(@event.World, @event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, new ItemStack(stack.ItemId, amount, stack.GetDamage()));
                entityItem.VelocityX = (float)random.NextGaussian() * DropSpread;
                entityItem.VelocityY = (float)random.NextGaussian() * DropSpread + 0.2F;
                entityItem.VelocityZ = (float)random.NextGaussian() * DropSpread;

                @event.World.Entities.SpawnEntity(entityItem);
            }
        }

        @event.World.Entities.RemoveBlockEntity(@event.X, @event.Y, @event.Z);
    }
}
