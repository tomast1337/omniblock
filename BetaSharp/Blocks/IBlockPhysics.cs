using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks;

/// <summary>Composable capability for neighbor updates, collision shapes, and placement/growth rules.</summary>
public interface IBlockPhysics
{
    void NeighborUpdate(Block block, OnTickEvent @event) { }

    /// <summary>Recomputes the block's metadata-driven bounding box for the given position.</summary>
    void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z) { }

    /// <summary>Sets the bounding box used when the block is rendered as an item (held/dropped).</summary>
    void SetupRenderBoundingBox(Block block) { }

    /// <summary>
    /// Additional placement restriction. Combined with (never replaces) the base
    /// replaceability check — returning true keeps the base verdict.
    /// </summary>
    bool CanPlaceAt(Block block, CanPlaceAtContext @event) => true;

    bool CanGrow(Block block, OnTickEvent @event) => true;

    /// <summary>
    /// Adds collision bounding boxes for blocks whose shape cannot be expressed as a single box
    /// (stairs, fences, etc.). When this returns any boxes, the base single-box collision path is
    /// skipped entirely.
    /// </summary>
    void AddCollisionBoxes(Block block, IBlockReader reader, int x, int y, int z, Box queryBox, List<Box> results) { }
}
