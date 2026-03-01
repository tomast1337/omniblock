using BetaSharp.Inventorys;
using BetaSharp.Items;

namespace BetaSharp.Recipes;

internal interface IRecipe
{
    bool Matches(InventoryCrafting InventoryCrafting);

    ItemStack GetCraftingResult(InventoryCrafting InventoryCrafting);

    int GetRecipeSize();

    ItemStack GetRecipeOutput();
}
