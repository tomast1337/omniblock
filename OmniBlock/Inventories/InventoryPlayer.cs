using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.NBT;

namespace OmniBlock.Inventories;

public class InventoryPlayer(EntityPlayer player) : IInventory
{
    private ItemStack? _cursorStack;
    public ItemStack?[] Armor = new ItemStack[4];
    public ItemStack?[] Main = new ItemStack[36];
    public int SelectedSlot;
    public EntityPlayer Player => player;

    public static int HotbarSize => 9;

    public ItemStack? ItemInHand =>
        SelectedSlot < HotbarSize && SelectedSlot >= 0 ? Main[SelectedSlot] : null;

    public ItemStack? RemoveStack(int slotIndex, int amount)
    {
        var targetArray = Main;
        if (slotIndex >= Main.Length)
        {
            targetArray = Armor;
            slotIndex -= Main.Length;
        }

        if (targetArray[slotIndex] != null)
        {
            ItemStack removeStack;
            var stack = targetArray[slotIndex];

            if (stack is null)
            {
                return null;
            }

            if (stack.Count <= amount)
            {
                removeStack = stack;
                targetArray[slotIndex] = null;
                return removeStack;
            }

            removeStack = stack.Split(amount);
            if (stack.Count == 0)
            {
                targetArray[slotIndex] = null;
            }

            return removeStack;
        }

        return null;
    }

    public void SetStack(int slotIndex, ItemStack? itemStack)
    {
        var targetArray = Main;
        if (slotIndex >= targetArray.Length)
        {
            slotIndex -= targetArray.Length;
            targetArray = Armor;
        }

        targetArray[slotIndex] = itemStack;
    }

    public int Size => Main.Length + 4;

    public ItemStack? GetStack(int slotIndex)
    {
        var targetArray = Main;
        if (slotIndex < targetArray.Length)
        {
            return targetArray[slotIndex];
        }

        slotIndex -= targetArray.Length;
        targetArray = Armor;

        return targetArray[slotIndex];
    }

    public string Name => "Inventory";

    public int MaxCountPerStack => 64;

    public void MarkDirty()
    {
    }

    public bool CanPlayerUse(EntityPlayer entityPlayer) => !Player.Dead && entityPlayer.GetSquaredDistance(Player) <= 64.0D;

    private int FindSlotByItemId(int itemId, int meta = -1)
    {
        for (var slotIndex = 0; slotIndex < Main.Length; ++slotIndex)
        {
            var stack = Main[slotIndex];
            if (stack != null && stack.ItemId == itemId && (meta < 0 || stack.GetDamage() == meta))
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private int StoreItemStack(ItemStack itemStack)
    {
        for (var slotIndex = 0; slotIndex < Main.Length; ++slotIndex)
        {
            var stack = Main[slotIndex];
            if (stack != null && stack.ItemId == itemStack.ItemId && stack.IsStackable() && stack.Count < stack.GetMaxCount() && stack.Count < MaxCountPerStack && (!stack.GetHasSubtypes() || stack.GetDamage() == itemStack.GetDamage()))
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private int GetFreeSlot(bool preferHandSlot = true)
    {
        if (preferHandSlot && Main[SelectedSlot] == null)
        {
            return SelectedSlot;
        }

        for (var slotIndex = 0; slotIndex < Main.Length; ++slotIndex)
        {
            if (Main[slotIndex] == null)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private int GetFreeHotbarSlot(bool preferHandSlot = true)
    {
        if (preferHandSlot && Main[SelectedSlot] == null)
        {
            return SelectedSlot;
        }

        for (var slotIndex = 0; slotIndex < HotbarSize; ++slotIndex)
        {
            if (Main[slotIndex] == null)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    public void SetCurrentItem(int itemId, int backupId = 0, int meta = -1, int backupMeta = -1)
    {
        var slotIndex = FindSlotByItemId(itemId, meta);
        if (slotIndex < 0)
        {
            if (Player.GameMode.FiniteResources)
            {
                if (backupId > 0)
                {
                    SetCurrentItem(backupId, meta: backupMeta);
                }

                return;
            }

            // move cursor to the next free so that item appears in hand.
            if (Main[SelectedSlot] != null)
            {
                var h = GetFreeHotbarSlot();
                if (h >= 0)
                {
                    SelectedSlot = h;
                }
            }

            Player.SendChatMessage(meta > 0 ? $"/give {itemId}:{meta}" : "/give " + itemId);
        }
        else if (slotIndex < HotbarSize)
        {
            SelectedSlot = slotIndex;
        }
    }

    public void ChangeCurrentItem(int scrollDirection)
    {
        if (scrollDirection > 0)
        {
            scrollDirection = 1;
        }

        if (scrollDirection < 0)
        {
            scrollDirection = -1;
        }

        for (SelectedSlot -= scrollDirection; SelectedSlot < 0; SelectedSlot += HotbarSize)
        {
        }

        while (SelectedSlot >= HotbarSize)
        {
            SelectedSlot -= HotbarSize;
        }
    }

    private int StorePartialItemStack(ItemStack itemStack)
    {
        var itemId = itemStack.ItemId;
        var remainingCount = itemStack.Count;
        var slotIndex = StoreItemStack(itemStack);
        if (slotIndex < 0)
        {
            slotIndex = GetFreeSlot();
        }

        if (slotIndex < 0)
        {
            return remainingCount;
        }

        var stack = Main[slotIndex] ??= new ItemStack(player.World.Content.Items, itemId, 0, itemStack.GetDamage());

        var spaceAvailable = remainingCount;
        if (remainingCount > stack.GetMaxCount() - stack.Count)
        {
            spaceAvailable = stack.GetMaxCount() - stack.Count;
        }

        if (spaceAvailable > MaxCountPerStack - stack.Count)
        {
            spaceAvailable = MaxCountPerStack - stack.Count;
        }

        if (spaceAvailable == 0)
        {
            return remainingCount;
        }

        remainingCount -= spaceAvailable;
        stack.Count += spaceAvailable;
        stack.AnimationTime = 5;
        return remainingCount;
    }

    public void Tick()
    {
        for (var slotIndex = 0; slotIndex < Main.Length; ++slotIndex)
        {
            Main[slotIndex]?.InventoryTick(Player.World, Player, slotIndex, SelectedSlot == slotIndex);
        }
    }

    public bool ConsumeInventoryItem(int itemId)
    {
        var slotIndex = FindSlotByItemId(itemId);
        if (slotIndex < 0) return false;

        var stack = Main[slotIndex];
        if (stack is not null && --stack.Count <= 0)
        {
            Main[slotIndex] = null;
        }

        return true;
    }

    public bool AddItemStackToInventory(ItemStack itemStack)
    {
        int slotIndex;
        if (itemStack.IsDamaged())
        {
            slotIndex = GetFreeSlot();
            if (slotIndex < 0) return false;

            var stack = itemStack.Copy();
            Main[slotIndex] = stack;
            stack.AnimationTime = 5;
            itemStack.Count = 0;
            return true;
        }

        do
        {
            slotIndex = itemStack.Count;
            itemStack.Count = StorePartialItemStack(itemStack);
        } while (itemStack.Count > 0 && itemStack.Count < slotIndex);

        return itemStack.Count < slotIndex;
    }

    public void AddItemStackToInventoryOrDrop(ItemStack itemStack)
    {
        if (AddItemStackToInventory(itemStack))
        {
            return;
        }

        Player.DropItem(itemStack);
    }

    public float GetStrVsBlock(Block block)
    {
        var miningSpeed = 1.0F;
        var stack = Main[SelectedSlot];
        if (stack != null)
        {
            miningSpeed *= stack.GetMiningSpeedMultiplier(block);
        }

        return miningSpeed;
    }

    public NBTTagList WriteToNBT(NBTTagList nbt)
    {
        int slotIndex;
        NBTTagCompound itemTag;
        for (slotIndex = 0; slotIndex < Main.Length; ++slotIndex)
        {
            var stack = Main[slotIndex];
            if (stack == null) continue;

            itemTag = new NBTTagCompound();
            itemTag.SetByte("Slot", (sbyte)slotIndex);
            stack.WriteToNbt(itemTag);
            nbt.SetTag(itemTag);
        }

        for (slotIndex = 0; slotIndex < Armor.Length; ++slotIndex)
        {
            var stack = Armor[slotIndex];

            if (stack == null) continue;

            itemTag = new NBTTagCompound();
            itemTag.SetByte("Slot", (sbyte)(slotIndex + 100));
            stack.WriteToNbt(itemTag);
            nbt.SetTag(itemTag);
        }

        return nbt;
    }

    public void ReadFromNBT(NBTTagList nbt)
    {
        Main = new ItemStack[36];
        Armor = new ItemStack[4];

        for (var i = 0; i < nbt.TagCount(); ++i)
        {
            var itemTag = (NBTTagCompound)nbt.TagAt(i);
            var slotIndex = itemTag.GetByte("Slot") & 255;
            ItemStack itemStack = new(player.World.Content.Items, itemTag);

            if (slotIndex >= 0 && slotIndex < Main.Length)
            {
                Main[slotIndex] = itemStack;
            }

            if (slotIndex >= 100 && slotIndex < Armor.Length + 100)
            {
                Armor[slotIndex - 100] = itemStack;
            }
        }
    }

    public int GetDamageVsEntity(Entity entity)
    {
        var itemStack = GetStack(SelectedSlot);
        return itemStack != null ? itemStack.GetAttackDamage(entity) : 1;
    }

    public bool CanHarvestBlock(Block block)
    {
        if (block.Material.IsHandHarvestable)
        {
            return true;
        }

        var itemStack = GetStack(SelectedSlot);
        return itemStack != null && itemStack.IsSuitableFor(block);
    }

    public ItemStack? ArmorItemBySlot(int slotIndex) => Armor[slotIndex];

    public int GetTotalArmorValue()
    {
        var totalArmor = 0;
        var durabilitySum = 0;
        var totalMaxDurability = 0;

        foreach (var stack in Armor)
        {
            if (stack?.GetItem().GetBehavior<ArmorBehavior>() is not { } armor)
            {
                continue;
            }

            var maxDurability = stack.GetMaxDamage();
            var pieceDamage = stack.GetDamage2();
            var remainingDurability = maxDurability - pieceDamage;
            durabilitySum += remainingDurability;
            totalMaxDurability += maxDurability;
            var armorValue = armor.DamageReduceAmount;
            totalArmor += armorValue;
        }

        if (totalMaxDurability == 0)
        {
            return 0;
        }

        return (totalArmor - 1) * durabilitySum / totalMaxDurability + 1;
    }

    public void DamageArmor(int durabilityLoss)
    {
        for (var slotIndex = 0; slotIndex < Armor.Length; ++slotIndex)
        {
            var stack = Armor[slotIndex];
            if (stack?.GetItem().GetBehavior<ArmorBehavior>() == null)
            {
                continue;
            }

            stack.DamageItem(durabilityLoss, Player);
            if (stack.Count != 0)
            {
                continue;
            }

            ItemStack.OnRemoved(Player);
            Armor[slotIndex] = null;
        }
    }

    public void DropInventory()
    {
        int slotIndex;
        for (slotIndex = 0; slotIndex < Main.Length; ++slotIndex)
        {
            if (Main[slotIndex] == null)
            {
                continue;
            }

            Player.DropItem(Main[slotIndex], true);
            Main[slotIndex] = null;
        }

        for (slotIndex = 0; slotIndex < Armor.Length; ++slotIndex)
        {
            if (Armor[slotIndex] == null)
            {
                continue;
            }

            Player.DropItem(Armor[slotIndex], true);
            Armor[slotIndex] = null;
        }
    }

    public void SetCursorStack(ItemStack? itemStack)
    {
        _cursorStack = itemStack;
        Player.OnCursorStackChanged(itemStack);
    }

    public ItemStack? GetCursorStack() => _cursorStack;

    public bool Contains(ItemStack itemStack)
    {
        int slotIndex;
        for (slotIndex = 0; slotIndex < Armor.Length; ++slotIndex)
        {
            var stack = Armor[slotIndex];
            if (stack != null && stack.Equals(itemStack))
            {
                return true;
            }
        }

        for (slotIndex = 0; slotIndex < Main.Length; ++slotIndex)
        {
            var stack = Main[slotIndex];

            if (stack != null && stack.Equals(itemStack))
            {
                return true;
            }
        }

        return false;
    }
}
