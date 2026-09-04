using OmniBlock.Items.Behaviors;

namespace OmniBlock.Items;

public static class ItemFactory
{
    public static Item Create(
        ItemDefinition def,
        in ItemBuildContext context,
        IItemBehaviorProviderRegistry behaviorProviders)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(behaviorProviders);
        var item = CreateDraft(def, context);
        AttachBehavior(item, def, context, behaviorProviders);
        return item;
    }

    internal static Item CreateDraft(ItemDefinition def, in ItemBuildContext context)
    {
        var item = new Item(def.ProtocolId - 256);
        item.SetItemName(def.TranslationKey ?? def.Name);
        if (def.MaxStackSize != 64) item.SetMaxCount(def.MaxStackSize);
        if (def.MaxDurability > 0) item.SetMaxDamage(def.MaxDurability);
        // An unset TextureId keeps the implicit default the int field used to have.
        item.SetTextureId(string.IsNullOrEmpty(def.TextureId) ? 0 : context.ResolveItemTexture(def.TextureId));
        if (def.Handheld) item.SetHandheld();
        if (def.HasSubtypes) item.SetHasSubtypes(true);
        return item;
    }

    internal static void AttachBehavior(
        Item item,
        ItemDefinition def,
        in ItemBuildContext context,
        IItemBehaviorProviderRegistry behaviorProviders)
    {
        foreach (var definition in def.Behaviors)
        {
            if (!definition.TryGetProperty("Type", out var typeElement) || typeElement.GetString() is not { } typeName)
                throw new ArgumentException("Item behavior requires a string 'Type'.");
            item.AddBehavior(behaviorProviders.Build(ResourceLocation.Parse(typeName), definition, context));
        }
    }
}
