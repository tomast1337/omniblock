using OmniBlock.Items;
using OmniBlock.Loot;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Drives a mob's death drops from a declarative <see cref="LootTable" />.
/// </summary>
public sealed class LootTableBehavior(LootTable table) : IEntityLootBehavior
{
    public LootTable Table { get; } = table;

    public void DropLoot(EntityLiving self, Entity? killer)
    {
        foreach (ItemStack stack in Table.Roll(LootContext.ForMob(self, killer)))
        {
            self.DropItem(stack, 0.0F);
        }
    }
}
