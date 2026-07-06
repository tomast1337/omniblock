using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Face culling for transparent blocks: hides faces shared with a neighbor of the same block id
/// (glass panes against glass, portal against portal), unless <paramref name="hideAdjacentFaces"/>
/// disables the same-id check.
/// </summary>
public sealed class GlassVisualBehavior(bool hideAdjacentFaces) : IBlockVisuals
{
    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
        => (hideAdjacentFaces || reader.GetBlockId(x, y, z) != block.id) && defaultVisibility;
}
