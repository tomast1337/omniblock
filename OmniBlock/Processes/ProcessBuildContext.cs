using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Processes;

/// <summary>Read-only content dependencies available while compiling process definitions.</summary>
public readonly record struct ProcessBuildContext(
    IItemRuntimeView Items,
    IBlockRuntimeView Blocks)
{
    public Item ResolveItem(ResourceLocation key) =>
        Items?.Get(key) ?? throw MissingContext(nameof(Items));

    public Block ResolveBlock(ResourceLocation key) =>
        Blocks?.Get(key) ?? throw MissingContext(nameof(Blocks));

    public ItemStack ResolveItemStack(string reference, int count = 1, int defaultMeta = 0)
    {
        if (Items is null) throw MissingContext(nameof(Items));
        if (Items.TryParse(reference, out ItemStack? stack, count, defaultMeta)) return stack;
        throw new KeyNotFoundException($"Unknown process item/block reference '{reference}'.");
    }

    private static InvalidOperationException MissingContext(string dependency) =>
        new($"{nameof(ProcessBuildContext)} has no {dependency} dependency.");
}
