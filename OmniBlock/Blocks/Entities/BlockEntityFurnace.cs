using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Recipes;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Entities;

public class BlockEntityFurnace : BlockEntity, IInventory
{

    private ItemStack?[] _inventory = new ItemStack[3];
    protected override BlockEntityType Type => Furnace;
    public int BurnTime { get; set; }
    public int CookTime { get; set; }
    public int FuelTime { get; set; }

    public bool IsBurning => BurnTime > 0;

    public int Size => _inventory.Length;

    public ItemStack? GetStack(int slot)
    {
        return _inventory[slot];
    }

    public ItemStack? RemoveStack(int slot, int stack)
    {
        if (_inventory[slot] == null) return null;

        ItemStack removedStack;
        var iStack = _inventory[slot];

        if (iStack is null) return null;

        if (iStack.Count <= stack)
        {
            removedStack = iStack;
            _inventory[slot] = null;
            return removedStack;
        }

        removedStack = iStack.Split(stack);
        if (iStack.Count == 0) _inventory[slot] = null;

        return removedStack;
    }

    public void SetStack(int slot, ItemStack? stack)
    {
        _inventory[slot] = stack;
        if (stack != null && stack.Count > MaxCountPerStack) stack.Count = MaxCountPerStack;
    }

    public string Name => "Furnace";

    public int MaxCountPerStack => 64;

    public bool CanPlayerUse(EntityPlayer player)
    {
        return World!.Entities.GetBlockEntity<BlockEntityFurnace>(X, Y, Z) == this && player.GetSquaredDistance(X + 0.5D, Y + 0.5D, Z + 0.5D) <= 64.0D;
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        var itemList = nbt.GetTagList("Items");
        _inventory = new ItemStack[Size];

        for (var itemIndex = 0; itemIndex < itemList.TagCount(); ++itemIndex)
        {
            var itemTag = (NBTTagCompound)itemList.TagAt(itemIndex);
            var slot = itemTag.GetByte("Slot");
            if (slot >= 0 && slot < _inventory.Length) _inventory[slot] = new ItemStack(World!.Content.Items, itemTag);
        }

        BurnTime = nbt.GetShort("BurnTime");
        CookTime = nbt.GetShort("CookTime");
        FuelTime = GetFuelTime(_inventory[1]);
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        nbt.SetShort("BurnTime", (short)BurnTime);
        nbt.SetShort("CookTime", (short)CookTime);
        NBTTagList itemList = new();

        for (var slotIndex = 0; slotIndex < _inventory.Length; ++slotIndex)
        {
            var stack = _inventory[slotIndex];
            if (stack == null) continue;
            NBTTagCompound slotTag = new();
            slotTag.SetByte("Slot", (sbyte)slotIndex);
            stack.WriteToNbt(slotTag);
            itemList.SetTag(slotTag);
        }

        nbt.SetTag("Items", itemList);
    }

    public int GetCookTimeDelta(int multiplier)
    {
        return CookTime * multiplier / 200;
    }

    public int GetFuelTimeDelta(int multiplier)
    {
        if (FuelTime == 0) FuelTime = 200;
        return BurnTime * multiplier / FuelTime;
    }

    public override void Tick(EntityManager entities)
    {
        var wasBurning = BurnTime > 0;
        var stateChanged = false;
        if (BurnTime > 0) --BurnTime;

        if (!World!.IsRemote)
        {
            if (BurnTime == 0 && CanAcceptRecipeOutput())
            {
                FuelTime = BurnTime = GetFuelTime(_inventory[1]);
                if (BurnTime > 0)
                {
                    stateChanged = true;
                    var inv1 = _inventory[1];
                    if (inv1 != null)
                    {
                        --inv1.Count;
                        if (inv1.Count == 0) _inventory[1] = null;
                    }
                }
            }

            if (IsBurning && CanAcceptRecipeOutput())
            {
                ++CookTime;
                if (CookTime == 200)
                {
                    CookTime = 0;
                    CraftRecipe();
                    stateChanged = true;
                }
            }
            else
            {
                CookTime = 0;
            }

            if (wasBurning != BurnTime > 0)
            {
                stateChanged = true;
                var furnaceBlock = World.Content.Blocks.GetByProtocolId(World.Reader.GetBlockId(X, Y, Z));
                ((FurnaceBehavior)furnaceBlock.Visuals).UpdateLitState(BurnTime > 0, World, X, Y, Z);
            }
        }

        if (stateChanged) MarkDirty();
    }

    private bool CanAcceptRecipeOutput()
    {
        var input = _inventory[0];
        if (input is null) return false;

        var output = RecipesSmelting.Craft(input.GetItem().Id);
        if (output is null) return false;

        var slot2 = _inventory[2];
        if (slot2 is null) return true;
        if (!slot2.IsItemEqual(output)) return false;

        return slot2.Count < MaxCountPerStack &&
               slot2.Count < slot2.GetMaxCount() &&
               slot2.Count < output.GetMaxCount();
    }

    private void CraftRecipe()
    {
        if (!CanAcceptRecipeOutput()) return;

        var item1 = _inventory[0];

        if (item1 is null) return;

        var outputStack = RecipesSmelting.Craft(item1.GetItem().Id);

        if (outputStack == null) return;

        if (_inventory[2] == null)
        {
            _inventory[2] = outputStack.Copy();
        }
        else
        {
            var item2 = _inventory[2];
            if (item2 != null && item2.ItemId == outputStack.ItemId) item2.Count++;
        }

        item1 = _inventory[0];

        if (item1 is null) return;

        item1.Count--;
        if (item1.Count <= 0) _inventory[0] = null;
    }

    private int GetFuelTime(ItemStack? itemStack)
    {
        if (itemStack == null) return 0;
        var itemId = itemStack.GetItem().Id;
        return itemId < 256 && World!.Content.Blocks.GetByProtocolId(itemId).Material == MaterialRegistry.Get("wood") ? 300 :
            itemId == World!.Content.Items.Get(new ResourceLocation(Namespace.OmniBlock, "stick")).Id ? 100 :
            itemId == World.Content.Items.Get(new ResourceLocation(Namespace.OmniBlock, "coal")).Id ? 1600 :
            itemId == World.Content.Items.Get(new ResourceLocation(Namespace.OmniBlock, "bucket_lava")).Id ? 20000 :
            itemId == World!.Content.Blocks.Get("sapling").Id ? 100 : 0;
    }
}
