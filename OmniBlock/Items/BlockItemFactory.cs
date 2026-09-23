using OmniBlock.Blocks;

namespace OmniBlock.Items;

internal static class BlockItemFactory
{
    internal static Item Create(BlockDefinition definition, Block block)
    {
        Item item = definition.BlockItem.Type switch
        {
            "block" => new ItemBlock(block),
            "cloth" => new ItemCloth(block),
            "log" => new ItemLog(block),
            "slab" => new ItemSlab(block),
            "sapling" => new ItemSapling(block),
            "grass" => new ItemGrass(block),
            "leaves" => new ItemLeaves(block),
            "piston" => new ItemPiston(block),
            string type => throw new ArgumentException($"Unknown block-item type '{type}' for block '{definition.Namespace}:{definition.Name}'.")
        };

        return item.SetItemName(definition.BlockItem.TranslationKey
                                ?? definition.TranslationKey
                                ?? definition.Name)
            .SetAliases(definition.BlockItem.Aliases);
    }
}
