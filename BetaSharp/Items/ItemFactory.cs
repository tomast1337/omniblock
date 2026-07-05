namespace BetaSharp.Items;

public static class ItemFactory
{
    public static Item Create(ItemDefinition def)
    {
        var item = new Item(def.ProtocolId - 256);
        item.setItemName(def.TranslationKey ?? def.Name);
        if (def.MaxStackSize != 64) item.setMaxCount(def.MaxStackSize);
        if (def.MaxDurability > 0) item.setMaxDamage(def.MaxDurability);
        item.setTexturePosition(def.TextureX, def.TextureY);
        if (def.Handheld) item.setHandheld();
        if (def.HasSubtypes) item.setHasSubtypes(true);
        if (def.Behavior is not null) item.SetBehavior(def.Behavior.Build());
        if (def.CraftingReturnItemProtocolId.HasValue)
            item.setCraftingReturnItem(Item.ITEMS[def.CraftingReturnItemProtocolId.Value]!);

        return item;
    }
}
