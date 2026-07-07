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

    /// <summary>
    /// Overrides the single-box collision shape independent of the render bounding box (e.g.
    /// farmland collides as a full cube while rendering with a recessed top).
    /// </summary>
    Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape) => defaultShape;

    /// <summary>
    /// Coarse "does this block collide at all" flag consumed by movement/raycast pre-checks,
    /// independent of <see cref="GetCollisionShape"/> (e.g. fire has no collision shape at all).
    /// </summary>
    bool HasCollision(Block block, bool defaultHasCollision) => defaultHasCollision;

    /// <summary>Whether this block can catch fire from the given neighbor position (fire's own spread registry).</summary>
    bool IsFlammable(Block block, IBlockReader reader, int x, int y, int z, bool defaultFlammable) => defaultFlammable;
}
