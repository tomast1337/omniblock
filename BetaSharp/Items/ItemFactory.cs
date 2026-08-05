using BetaSharp.Textures;

namespace BetaSharp.Items;

public static class ItemFactory
{
    private static readonly AtlasTileMap s_itemTextures = AtlasTileMap.Load("textures/atlas/items.json");

    public static Item Create(ItemDefinition def)
    {
        var item = new Item(def.ProtocolId - 256);
        item.SetItemName(def.TranslationKey ?? def.Name);
        if (def.MaxStackSize != 64) item.SetMaxCount(def.MaxStackSize);
        if (def.MaxDurability > 0) item.SetMaxDamage(def.MaxDurability);
        // An unset TextureId keeps the implicit default the int field used to have.
        item.SetTextureId(string.IsNullOrEmpty(def.TextureId) ? 0 : s_itemTextures.IndexOf(ResourceLocation.Parse(def.TextureId).Path));
        if (def.Handheld) item.SetHandheld();
        if (def.HasSubtypes) item.SetHasSubtypes(true);
        if (def.Behavior is not null) item.SetBehavior(def.Behavior.Build());

        return item;
    }

    public static void ResolveCrossReferences(ItemDefinition def)
    {
        if (def.CraftingReturnItemProtocolId is { } returnId)
        {
            Item.Items[def.ProtocolId]!.SetCraftingReturnItem(Item.Items[returnId]!);
        }
    }
}
