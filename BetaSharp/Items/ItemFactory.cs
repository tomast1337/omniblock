using System.Text;
using BetaSharp.Registries;

namespace BetaSharp.Items;

public static class ItemFactory
{
    public static Item Create(ItemDefinition def)
    {
        var item = new Item(def.ProtocolId - 256);
        item.setItemName(def.TranslationKey);
        if (def.MaxStackSize != 64) item.setMaxCount(def.MaxStackSize);
        if (def.MaxDurability > 0) item.setMaxDamage(def.MaxDurability);
        item.setTexturePosition(def.TextureX, def.TextureY);
        if (def.Handheld) item.setHandheld();
        if (def.HasSubtypes) item.setHasSubtypes(true);
        if (def.Behavior is not null) item.SetBehavior(def.Behavior.Build());
        if (def.CraftingReturnItemProtocolId.HasValue)
            item.setCraftingReturnItem(Item.ITEMS[def.CraftingReturnItemProtocolId.Value]!);

        ResourceLocation location = BuildResourceLocation(def);
        def.Name = location.Path;
        def.Namespace = location.Namespace;
        DefaultRegistries.Items.Register(def.ProtocolId, location, def);

        return item;
    }

    private static ResourceLocation BuildResourceLocation(ItemDefinition def)
    {
        string path = ToSnakeCase(def.TranslationKey);
        if (DefaultRegistries.Items.ContainsKey(ResourceLocation.Parse($"betasharp:{path}")))
        {
            path = $"{path}_{def.ProtocolId}";
        }

        return ResourceLocation.Parse($"betasharp:{path}");
    }

    private static string ToSnakeCase(string value)
    {
        var sb = new StringBuilder(value.Length + 4);
        foreach (char c in value)
        {
            if (char.IsUpper(c))
            {
                if (sb.Length > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
