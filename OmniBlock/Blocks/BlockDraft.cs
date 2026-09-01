namespace OmniBlock.Blocks;

/// <summary>Mutable construction state discarded when its block is finalized.</summary>
internal sealed class BlockDraft
{
    internal BlockDraft(Block block)
    {
        Block = block;
        TextureId = block.TextureId;
        SoundGroup = block.SoundGroup;
        TopVariance = block.TopVariance;
        BottomVariance = block.BottomVariance;
        SideVariance = block.SideVariance;
        Ticker = block.Ticker;
        Interactable = block.Interactable;
        Visuals = block.Visuals;
        Lifecycle = block.Lifecycle;
        Physics = block.Physics;
        Redstone = block.Redstone;
        RenderType = block.RenderType;
        HasCollisionBox = block.HasCollisionBox;
        BurnChance = block.BurnChance;
        SpreadChance = block.SpreadChance;
        IsOpaque = block.IsOpaque;
        TickRate = block.TickRate;
        RenderLayer = block.RenderLayer;
        BlockName = block.BlockName.StartsWith("tile.", StringComparison.Ordinal)
            ? block.BlockName[5..]
            : block.BlockName;
        EnableStats = block.EnableStats;
        IgnoreMetaUpdates = block.IgnoreMetaUpdates;
        TickRandomly = block.TickRandomly;
        Opacity = block.Opacity;
        Luminance = block.Luminance;
        DropCount = block.DropCount;
        PreservesMetaOnDrop = block.PreservesMetaOnDrop;
    }

    internal Block Block { get; }
    internal int TextureId { get; set; }
    internal BlockSoundGroup SoundGroup { get; set; }
    internal TextureVariance TopVariance { get; set; }
    internal TextureVariance BottomVariance { get; set; }
    internal TextureVariance SideVariance { get; set; }
    internal IBlockTicker? Ticker { get; set; }
    internal IBlockInteractable? Interactable { get; set; }
    internal IBlockVisuals? Visuals { get; set; }
    internal IBlockLifecycle? Lifecycle { get; set; }
    internal IBlockPhysics? Physics { get; set; }
    internal IRedstoneComponent? Redstone { get; set; }
    internal BlockRendererType RenderType { get; set; }
    internal bool HasCollisionBox { get; set; }
    internal byte BurnChance { get; set; }
    internal byte SpreadChance { get; set; }
    internal bool IsOpaque { get; set; }
    internal int TickRate { get; set; }
    internal int RenderLayer { get; set; }
    internal string BlockName { get; set; }
    internal bool EnableStats { get; set; }
    internal bool IgnoreMetaUpdates { get; set; }
    internal bool TickRandomly { get; set; }
    internal int Opacity { get; set; }
    internal float Luminance { get; set; }
    internal int DropCount { get; set; }
    internal bool PreservesMetaOnDrop { get; set; }

    internal void Apply()
    {
        Block.ApplyDraft(this);
    }
}