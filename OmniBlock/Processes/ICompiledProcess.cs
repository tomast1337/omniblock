using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Processes;

/// <summary>
/// Immutable provider-owned process produced during content construction. Consumers narrow this
/// value to the contract understood by their crafting station or machine.
/// </summary>
public interface ICompiledProcess
{
    ResourceLocation Id { get; }
    ResourceLocation ProviderType { get; }
}

/// <summary>Immutable item-stack description held by a compiled process.</summary>
public readonly record struct ProcessItemStack(Item Item, int Count, int Metadata)
{
    public ItemStack CreateStack() => new(Item, Count, Metadata);

    public bool Matches(ItemStack stack) =>
        stack.ItemId == Item.Id && (Metadata == -1 || stack.GetDamage() == Metadata);

    internal static ProcessItemStack FromStack(ItemStack stack) =>
        new(stack.GetItem(), stack.Count, stack.GetDamage());
}

public interface ICompiledCraftingProcess : ICompiledProcess
{
    ProcessItemStack Output { get; }
    int IngredientCount { get; }
    bool Matches(InventoryCrafting input);
    ItemStack CreateResult();
}

public interface ICompiledSmeltingProcess : ICompiledProcess
{
    ProcessItemStack Input { get; }
    ProcessItemStack Output { get; }
    bool Matches(ItemStack input);
    ItemStack CreateResult();
}
