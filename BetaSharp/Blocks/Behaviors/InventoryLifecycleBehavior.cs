using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Inventorys;
using BetaSharp.Items;
using BetaSharp.Util.Maths;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Shared behavior for blocks whose tile entity implements <see cref="IInventory"/>: scatters
/// inventory contents on break and creates the entity on placement. The public
/// <see cref="IgnoreBlockRemoval"/> flag is used by <c>BlockFurnace.updateLitState</c> to
/// suppress drops during the lit/unlit id swap.
/// Assign to the Lifecycle slot.
/// </summary>
public sealed class InventoryLifecycleBehavior : IBlockLifecycle
{
    private const float DropSpread = 0.05F;
    public static readonly ThreadLocal<bool> IgnoreBlockRemoval = new(() => false);

    private static readonly ThreadLocal<JavaRandom> s_random = new(() => new JavaRandom());
    private static readonly ILogger<InventoryLifecycleBehavior> s_logger = BetaSharp.Log.Instance.For<InventoryLifecycleBehavior>();

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (block.getBlockEntity() is { } blockEntity)
        {
            @event.World.Entities.SetBlockEntity(@event.X, @event.Y, @event.Z, blockEntity);
        }
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        if (IgnoreBlockRemoval.Value) return;

        BlockEntity? entity = @event.World.Entities.GetBlockEntity<BlockEntity>(@event.X, @event.Y, @event.Z);
        if (entity is not IInventory inventory)
        {
            if (entity != null)
            {
                // Entity exists but isn't an inventory — still clean it up.
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
                EntityItem entityItem = new(@event.World, @event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, new ItemStack(stack.ItemId, amount, stack.getDamage()))
                {
                    VelocityX = (float)random.NextGaussian() * DropSpread,
                    VelocityY = (float)random.NextGaussian() * DropSpread + 0.2F,
                    VelocityZ = (float)random.NextGaussian() * DropSpread
                };

                @event.World.Entities.SpawnEntity(entityItem);
            }
        }

        @event.World.Entities.RemoveBlockEntity(@event.X, @event.Y, @event.Z);
    }
}
