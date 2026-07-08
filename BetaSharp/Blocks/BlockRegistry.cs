namespace BetaSharp.Blocks;

public static class BlockRegistry
{
    /// <summary>
    ///     Forwards to the existing <see cref="Block.ByName(string)" /> resolver — deliberately not
    ///     a second, differently-named lookup.
    /// </summary>
    public static Block Get(string name) => Block.ByName(name)
        ?? throw new ArgumentException($"Unknown block: '{name}'");

    internal static void LoadAndBuild(IEnumerable<BlockDefinition> definitions)
    {
        List<BlockDefinition> defs = definitions.ToList();

        // Pass 1: construct every plain Block. Block's constructor plus the fluent setters
        // BlockFactory.Create calls already populate every hot-path array (Blocks,
        // BlocksOpaque, BlockLightOpacity, BlocksAllowVision, BlocksRandomTick,
        // BlocksIgnoreMetaUpdate, BlocksLightLuminance) — nothing further to do here, and
        // re-deriving them a second time here would just be a second place to drift out of
        // sync with those setters.
        foreach (BlockDefinition def in defs)
        {
            BlockFactory.Create(def);
        }

        // Pass 2: resolve behaviors, loot tables, and cross-block references now that every
        // Block exists and is reachable by Block.ByName.
        foreach (BlockDefinition def in defs)
        {
            BlockFactory.AttachBehaviors(Block.Blocks[def.ProtocolId]!, def);
        }
    }
}
