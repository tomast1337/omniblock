using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Network.Packets;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

public interface IItemBehavior
{
    void Apply(Item item)
    {
    }

    float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block) => 1.0f;
    bool PostHit(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player) => false;
    bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player) => false;
    int GetAttackDamage(Item item, Entity entity) => 1;
    bool IsSuitableFor(Item item, Block block) => false;
    ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player) => itemStack;
    bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta) => false;

    void UseOnEntity(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
    }

    bool IsHandheld(Item item) => false;
    bool IsHandheldRod(Item item) => false;
    int GetTextureId(Item item, int meta) => item._textureId;
    string GetItemNameIS(Item item, ItemStack itemStack) => item.getItemName();
    IReadOnlyList<string> GetItemAliases(Item item) => [];

    void InventoryTick(Item item, ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)
    {
    }

    void OnCraft(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
    }

    bool IsNetworkSynced(Item item) => false;
    Packet? GetUpdatePacket(Item item, ItemStack stack, IWorldContext world, EntityPlayer player) => null;
}
