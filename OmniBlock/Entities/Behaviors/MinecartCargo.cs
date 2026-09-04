using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     The chest a chest minecart carries. Its own object rather than something the cart entity
///     implements, because only one of the three cart kinds has one and <see cref="EntityObject" />
///     is shared by every non-living entity.
/// </summary>
public sealed class MinecartCargo(Entity cart) : IInventory
{
    private readonly ItemStack?[] _slots = new ItemStack[36];

    /// <summary>Every slot, including those past <see cref="Size" />, for saving and scattering.</summary>
    public int SlotCount => _slots.Length;

    public int Size => 27;

    public string Name => "Minecart";

    public int MaxCountPerStack => 64;

    public ItemStack? GetStack(int slotIndex) => _slots[slotIndex];

    public void SetStack(int slotIndex, ItemStack? itemStack)
    {
        _slots[slotIndex] = itemStack;
        if (itemStack != null && itemStack.Count > MaxCountPerStack)
        {
            itemStack.Count = MaxCountPerStack;
        }
    }

    public ItemStack? RemoveStack(int slotIndex, int amount)
    {
        if (_slots[slotIndex] is not { } stack)
        {
            return null;
        }

        if (stack.Count <= amount)
        {
            _slots[slotIndex] = null;
            return stack;
        }

        var taken = stack.Split(amount);
        if (stack.Count == 0)
        {
            _slots[slotIndex] = null;
        }

        return taken;
    }

    public void MarkDirty()
    {
    }

    /// <summary>Reachable while the cart is alive and the player is within eight blocks of it.</summary>
    public bool CanPlayerUse(EntityPlayer player) => !cart.Dead && player.GetSquaredDistance(cart) <= 64.0D;
}
